using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using AsyncAwaitBestPractices;
using Depository.Abstraction.Interfaces;
using Depository.Abstraction.Interfaces.NotificationHub;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Transitions;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
///     播放控制服务 — 封装底层 <see cref="IPlayer" /> 操作，协调播放状态更新。
///     <para>
///         通过 <see cref="PlaybackStateService" /> 写入播放状态，并通过 owner service events 发布业务事件。
///     </para>
/// </summary>
public sealed partial class PlaybackControlService : IPlaybackControlService,
    INotificationSubscriber<PlaybackRequestFailedNotification>,
    IDisposable,
    IAsyncDisposable
{
    private readonly ChopinAudioService _audioService;

    private readonly SemaphoreSlim _autoSkipLock = new(1, 1);
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly ILyricService _lyricService;
    private readonly IReadOnlyList<IMusicResourceProvidable> _musicResourceProviders;
    private readonly IPlaybackNotificationService _playbackNotification;
    private readonly PlayCoreBase _playCore;
    private readonly INotificationHub _playCoreNotificationHub;

    private readonly IPlayer _player;
    private readonly PlaybackSettings _playbackSettings;
    private readonly LastFMSettings _lastFmSettings;
    private readonly SmtcPlaybackCommandDispatcher _smtcCommandDispatcher;
    private readonly PlaybackStateService _state;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly IReadOnlyDictionary<string, ITrackTransition> _transitions;
    private readonly IUIThreadDispatcher _uiThreadDispatcher;
    private ITrackTransition _activeTransition;
    private IPlaybackSource? _currentSource;
    private bool _disposed;
    private bool _initialized;
    private long _lastScrobbledGeneration = -1;
    private CancellationTokenSource? _lyricCts;
    private CancellationTokenSource? _playbackCts;
    private long _playbackGeneration;
    private SystemMediaTransportControls? _smtc;
    private bool _smtcSubscribed;

    /// <summary>
    ///     创建 <see cref="PlaybackControlService" /> 实例。
    /// </summary>
    /// <param name="player">底层音频播放器（AudioGraphPlayer）</param>
    /// <param name="mediaSourceService">媒体源路由服务</param>
    /// <param name="state">播放状态中心</param>
    /// <param name="setting">应用设置</param>
    public PlaybackControlService(
        IPlayer player,
        PlayCoreBase playCore,
        INotificationHub playCoreNotificationHub,
        PlaybackStateService state,
        PlaybackSettings playbackSettings,
        LastFMSettings lastFmSettings,
        ILyricService lyricService,
        IUIThreadDispatcher uiThreadDispatcher,
        IPlaybackNotificationService playbackNotification,
        IBackgroundTaskRunner taskRunner,
        IEnumerable<IMusicResourceProvidable> musicResourceProviders,
        ChopinAudioService audioService,
        IEnumerable<ITrackTransition> transitions)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _playCore = playCore ?? throw new ArgumentNullException(nameof(playCore));
        _playCoreNotificationHub =
            playCoreNotificationHub ?? throw new ArgumentNullException(nameof(playCoreNotificationHub));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _playbackSettings = playbackSettings ?? throw new ArgumentNullException(nameof(playbackSettings));
        _lastFmSettings = lastFmSettings ?? throw new ArgumentNullException(nameof(lastFmSettings));
        _lyricService = lyricService ?? throw new ArgumentNullException(nameof(lyricService));
        _uiThreadDispatcher = uiThreadDispatcher ?? throw new ArgumentNullException(nameof(uiThreadDispatcher));
        _playbackNotification = playbackNotification ?? throw new ArgumentNullException(nameof(playbackNotification));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
        _musicResourceProviders = musicResourceProviders?.ToList() ??
                                  throw new ArgumentNullException(nameof(musicResourceProviders));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _transitions = transitions?.ToDictionary(transition => transition.Id)
                       ?? throw new ArgumentNullException(nameof(transitions));
        _activeTransition = _transitions.TryGetValue(_playbackSettings.TransitionId, out var transition)
            ? transition
            : _transitions["dir"];
        _smtcCommandDispatcher = new SmtcPlaybackCommandDispatcher(
            PlayCoreAfterInitializationAsync,
            PauseCoreAsync,
            () => MoveNextAndPlayAsync(true),
            MovePreviousAndPlayAsync);
        _state.ActiveTransitionId = _activeTransition.Id;

        if (_player is AudioGraphPlayer graphPlayer)
        {
            graphPlayer.OnTrackReachesEnd += OnTrackReachesEnd;
            graphPlayer.OnGlobalPlaybackStatusChanged += OnGlobalPlaybackStatusChanged;
            graphPlayer.OnPositionChanged += OnPositionChanged;
            graphPlayer.OnPrimaryPlaybackSourceChanged += OnPrimaryPlaybackSourceChanged;
        }

        _state.PropertyChanged += OnPlaybackStatePropertyChanged;
    }

    public event EventHandler<SeekRequestedEventArgs>? SeekRequested;

    public async Task MoveNextAndPlayAsync(bool userInitiated)
    {
        await MoveNextAndPlayAsync(userInitiated, true).ConfigureAwait(false);
    }

    public async Task MovePreviousAndPlayAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            if ((await _playCore.GetPlaylistAsync().ConfigureAwait(false)).Count == 0)
                return;

            await _playCore.MovePreviousAsync().ConfigureAwait(false);
            if (_playCore.CurrentSong is { } song)
                await LoadAndPlayCoreAsync(song, true, false, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task MoveNextAndPlayAsync(bool userInitiated, bool repeatSingleOnAutoAdvance)
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await MoveNextAndPlayCoreAsync(
                    userInitiated,
                    repeatSingleOnAutoAdvance,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task MoveNextAndPlayCoreAsync(
        bool userInitiated,
        bool repeatSingleOnAutoAdvance,
        CancellationToken ct)
    {
        var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
        if (queue.Count == 0)
            return;

        if (_playCore.ActivePlayModeId == "pfm")
        {
            var currentIndex = _state.NowPlayingIndex >= 0
                ? _state.NowPlayingIndex
                : await _playCore.GetCurrentIndexAsync().ConfigureAwait(false);
            if (currentIndex + 1 >= queue.Count)
                await PersonalFM.AppendMoreTracksAsync().ConfigureAwait(false);
        }

        if (_playCore.ActivePlayModeId == "ltg" && ListenTogetherManager.Instance?.ServerNextIndex is { } serverIndex)
        {
            ListenTogetherManager.Instance.ServerNextIndex = null;
            await _playCore.MovePointerToIndexAsync(serverIndex).ConfigureAwait(false);
        }
        else if (repeatSingleOnAutoAdvance
                 && _playCore.ActivePlayModeId == "sgl"
                 && _state.NowPlayingProviderItem is not null
                 && !userInitiated)
        {
            await _playCore.SeekAsync(0, ct).ConfigureAwait(false);
            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(TimeSpan.Zero));
            await _playCore.PlayAsync(ct).ConfigureAwait(false);
            CaptureCurrentPlaybackIdentity();
            return;
        }
        else
        {
            await _playCore.MoveNextAsync().ConfigureAwait(false);
        }

        if (_playCore.CurrentSong is { } song)
            await LoadAndPlayCoreAsync(song, true, false, ct).ConfigureAwait(false);
    }

    private async Task<TransitionPreparedTrack?> PrepareNextCoreAsync(
        TrackTransitionContext context,
        CancellationToken ct)
    {
        if (!IsCurrentGeneration(context.Source, context.Generation)
            || !context.CanPreload)
            return null;

        var queue = await _playCore.GetOrderedPlaylistAsync(ct).ConfigureAwait(false);
        var currentIndex = await _playCore.GetCurrentIndexAsync(ct).ConfigureAwait(false);
        if (_playCore.ActivePlayModeId == "pfm" && currentIndex + 1 >= queue.Count)
        {
            await PersonalFM.AppendMoreTracksAsync().ConfigureAwait(false);
            queue = await _playCore.GetOrderedPlaylistAsync(ct).ConfigureAwait(false);
        }

        if (queue.Count == 0 || currentIndex < 0)
            return null;

        var queueRevision = _state.QueueRevision;
        var nextIndex = (currentIndex + 1) % queue.Count;
        var nextSong = await _playCore.GetSongAtAsync(nextIndex, ct).ConfigureAwait(false);
        if (nextSong is null)
            return null;

        var ticket = await _playCore.PreparePlaybackAsync(nextSong, ct).ConfigureAwait(false);
        if (ticket is null)
            return null;
        if (queueRevision != _state.QueueRevision
            || !IsCurrentGeneration(context.Source, context.Generation))
        {
            await ticket.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        return new TransitionPreparedTrack
        {
            Song = nextSong,
            Ticket = ticket,
            Generation = context.Generation,
            Source = context.Source,
            CurrentIndex = currentIndex,
            QueueRevision = queueRevision
        };
    }

    private async Task<PreparedPlaybackPromotion?> PromoteCoreAsync(
        TransitionPreparedTrack prepared,
        CancellationToken ct)
    {
        if (!IsCurrentGeneration(prepared.Source, prepared.Generation)
            || prepared.QueueRevision != _state.QueueRevision
            || await _playCore.GetCurrentIndexAsync(ct).ConfigureAwait(false) != prepared.CurrentIndex)
            return null;

        var queue = await _playCore.GetOrderedPlaylistAsync(ct).ConfigureAwait(false);
        if (queue.Count == 0)
            return null;

        var nextIndex = (prepared.CurrentIndex + 1) % queue.Count;
        var nextSong = await _playCore.GetSongAtAsync(nextIndex, ct).ConfigureAwait(false);
        if (!SameSong(nextSong, prepared.Song))
            return null;

        var oldSong = _state.NowPlayingProviderItem;
        await _playCore.MoveNextAsync(ct).ConfigureAwait(false);
        if (!SameSong(_playCore.CurrentSong, prepared.Song))
        {
            await _playCore.MovePointerToIndexAsync(prepared.CurrentIndex, ct).ConfigureAwait(false);
            return null;
        }

        await SetCurrentSongAsync(prepared.Song, ct).ConfigureAwait(false);
        PreparedPlaybackPromotion? promotion;
        try
        {
            promotion = await _playCore
                .PromotePreparedPlaybackAsync(prepared.Ticket, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            promotion = null;
        }

        if (promotion is null)
        {
            await _playCore.MovePointerToIndexAsync(prepared.CurrentIndex, ct).ConfigureAwait(false);
            if (oldSong is not null)
                await SetCurrentSongAsync(oldSong, ct).ConfigureAwait(false);
            return null;
        }

        CaptureCurrentPlaybackIdentity();
        _state.IsPlaying = true;

        if (_lastFmSettings.LastFMScrobble
            && oldSong is not null
            && _lastScrobbledGeneration != prepared.Generation)
        {
            _lastScrobbledGeneration = prepared.Generation;
            _taskRunner.Forget(
                ScrobbleSafeAsync(oldSong),
                "scrobble cross-faded track");
        }

        return promotion;
    }

    private async Task ScrobbleSafeAsync(SingleSongBase song)
    {
        try
        {
            await _playbackNotification.ScrobbleAsync(song).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Last.fm scrobble failed: {ex}");
        }
    }

    private static bool SameSong(SingleSongBase? left, SingleSongBase? right)
    {
        return ReferenceEquals(left, right)
               || (left is not null
                   && right is not null
                   && left.ProviderId == right.ProviderId
                   && left.TypeId == right.TypeId
                   && left.ActualId == right.ActualId);
    }

    private static int FindSongIndex(IReadOnlyList<SingleSongBase> songs, SingleSongBase targetSong)
    {
        for (var i = 0; i < songs.Count; i++)
            if (SameSong(songs[i], targetSong))
                return i;

        return -1;
    }

    internal static bool CanReplaceQueueForCurrentSong(
        SingleSongBase? nowPlayingSong,
        SingleSongBase? currentQueueSong,
        SingleSongBase expectedCurrentSong)
    {
        return SameSong(nowPlayingSong, expectedCurrentSong)
               && SameSong(currentQueueSong, expectedCurrentSong);
    }

    internal static List<SingleSongBase> CreateQueuePreservingCurrentSong(
        IReadOnlyList<SingleSongBase> songs,
        SingleSongBase currentSong)
    {
        var replacementSongs = songs.ToList();
        var currentIndex = FindSongIndex(replacementSongs, currentSong);
        if (currentIndex >= 0)
            replacementSongs[currentIndex] = currentSong;

        return replacementSongs;
    }

    private sealed class TransitionHost : ITrackTransitionHost
    {
        private readonly PlaybackControlService _owner;

        public TransitionHost(PlaybackControlService owner)
        {
            _owner = owner;
        }

        public Task<TransitionPreparedTrack?> PrepareNextAsync(
            TrackTransitionContext context,
            CancellationToken ct)
        {
            return _owner.PrepareNextCoreAsync(context, ct);
        }

        public Task<PreparedPlaybackPromotion?> PromoteAsync(
            TransitionPreparedTrack prepared,
            CancellationToken ct)
        {
            return _owner.PromoteCoreAsync(prepared, ct);
        }

        public Task AdvanceDirectAsync(TrackTransitionContext context, CancellationToken ct)
        {
            if (!_owner.IsCurrentGeneration(context.Source, context.Generation))
                return Task.CompletedTask;

            return _owner.MoveNextAndPlayCoreAsync(
                false,
                true,
                ct);
        }
    }

    #region IPlaybackControlService

    /// <inheritdoc />
    public bool IsPlaying => _state.IsPlaying;

    /// <inheritdoc />
    public TimeSpan Position => _state.Position;

    public async Task SetTransitionAsync(string transitionId, CancellationToken ct = default)
    {
        if (!_transitions.TryGetValue(transitionId, out var transition))
            throw new ArgumentOutOfRangeException(nameof(transitionId));

        await _transitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            _activeTransition = transition;
            _state.ActiveTransitionId = transition.Id;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async Task SetPlayModeAsync(string playModeId, CancellationToken ct = default)
    {
        await _transitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await _playCore.SetPlayModeAsync(playModeId, ct).ConfigureAwait(false);
            _state.ActiveStrategyId = _playCore.ActivePlayModeId;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async Task ClearQueueAsync(CancellationToken ct = default)
    {
        await _transitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await _playCore.RemoveAllSongAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async Task<bool> ReplaceQueueKeepingPlaybackAsync(
        IReadOnlyList<SingleSongBase> songs,
        SingleSongBase expectedCurrentSong,
        string? playSourceId,
        CancellationToken ct = default)
    {
        if (songs.Count == 0 || _playCore.CurrentPlayList is null)
            return false;

        if (FindSongIndex(songs, expectedCurrentSong) < 0)
            return false;

        await _transitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // On a cold start the first PlayAsync can complete before AudioGraph publishes
            // PrimaryPlaybackSource. The queue itself is already safe to complete at that
            // point, so validate the logical current item instead of asynchronous player
            // status/source mirrors. Both identities must still match to prevent an older
            // background build from replacing a queue after the user selected another song.
            if (!CanReplaceQueueForCurrentSong(
                    _state.NowPlayingProviderItem,
                    _playCore.CurrentSong,
                    expectedCurrentSong))
                return false;

            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            var currentSong = _playCore.CurrentSong!;
            var replacementSongs = CreateQueuePreservingCurrentSong(songs, currentSong);
            await _playCore.CurrentPlayList
                .SetSongListAsync(replacementSongs, ct)
                .ConfigureAwait(false);
            // SetSongListAsync may rebuild an ordered/shuffled projection. Locate by identity
            // in that projection instead of applying an index from the source-order list.
            // Keeping the exact current-song instance is also essential: PlayCore associates
            // its active audio ticket with that reference, and changing it would make Seek
            // dispose the playing ticket and create a paused replacement source.
            await _playCore.MovePointerToAsync(currentSong, ct).ConfigureAwait(false);
            _playCore.PlaySourceId = playSourceId ?? string.Empty;
            return true;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <inheritdoc />
    public double Volume
    {
        get => _state.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            _player.SetOutputVolume(clamped);
            _playbackSettings.Volume = (int)(clamped * 100);
            _state.Volume = clamped;
        }
    }

    public void SetAudioGainEnabled(bool enabled)
    {
        _audioService.SetAudioGainEnabled(enabled);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initializeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await _player.InitializePlayer(new AudioGraphAudioSetting
            {
                DefaultDeviceId = _playbackSettings.AudioRenderDevice,
                OutputVolume = _playbackSettings.Volume / 100d,
                AutoFallback = true,
                EnableFFTProcessing = _playbackSettings.EnableFFT || _playbackSettings.ShowSpectrum
            }).ConfigureAwait(false);

            if (_player is AudioGraphPlayer graphPlayer)
            {
                SystemMediaTransportControls? smtc = null;
                var uiInitialized = await _uiThreadDispatcher.TryRunAsync(() =>
                {
                    smtc = SystemMediaTransportControls.GetForCurrentView();
                    smtc.IsPlayEnabled = true;
                    smtc.IsPauseEnabled = true;
                    smtc.IsNextEnabled = true;
                    smtc.IsPreviousEnabled = true;
                    smtc.IsEnabled = true;
                    smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
                    smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                    graphPlayer.SMTCManager = new SMTCManager(smtc);
                    _smtc = smtc;

                    if (!_smtcSubscribed)
                    {
                        smtc.ButtonPressed += SMTC_ButtonPressed;
                        smtc.PlaybackPositionChangeRequested += SMTC_PlaybackPositionChangeRequested;
                        _smtcSubscribed = true;
                    }
                });
                if (!uiInitialized)
                    throw new InvalidOperationException("SMTC initialization requires an active UI view.");
                if (smtc is null)
                    throw new InvalidOperationException("SMTC initialization did not return a control instance.");
            }

            _state.Volume = _playbackSettings.Volume / 100d;
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private void SMTC_PlaybackPositionChangeRequested(SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        SeekAsync(args.RequestedPlaybackPosition).SafeFireAndForget();
    }

    private void SMTC_ButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        _taskRunner.Forget(
            Task.Run(() => _smtcCommandDispatcher.DispatchAsync(button)),
            $"SMTC {button}");
    }

    /// <inheritdoc />
    public void Play()
    {
        _taskRunner.Forget(PlayCoreAfterInitializationAsync(), "play via PlayCore");
    }

    private async Task PlayCoreAfterInitializationAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await _playCore.PlayAsync().ConfigureAwait(false);

            if (_playCore.CurrentPlayingTicket is null)
            {
                _state.IsPlaying = false;
                return;
            }

            // Playback-memory restore intentionally loads the startup song with
            // autoPlay:false. The later Play command creates its first audio ticket here,
            // so this path must establish the same source/generation identity as a normal
            // LoadAndPlay call; otherwise the first track-end event is rejected as stale.
            CaptureCurrentPlaybackIdentity();
            _state.IsPlaying = true;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        _taskRunner.Forget(PauseCoreAsync(), "pause via PlayCore");
    }

    private async Task PauseCoreAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await _playCore.PauseAsync().ConfigureAwait(false);
            _state.IsPlaying = false;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _transitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken ct)
    {
        await _playCore.StopAsync(ct).ConfigureAwait(false);
        _currentSource = null;
        _playbackGeneration++;
        _state.IsPlaying = false;
    }

    /// <inheritdoc />
    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    /// <inheritdoc />
    public async Task SeekAsync(TimeSpan target)
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            await _playCore.SeekAsync((long)target.TotalMilliseconds);

            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(target));

            // 与原始实现一致，等待 seek 稳定
            await Task.Delay(500);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task LoadAndPlayAsync(SingleSongBase song, bool autoPlay = true, bool removeCurrentSongs = true)
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();
        var ct = _playbackCts.Token;

        try
        {
            await InitializeAsync().ConfigureAwait(false);
            await _transitionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
                await LoadAndPlayCoreAsync(song, autoPlay, removeCurrentSongs, ct).ConfigureAwait(false);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SetCurrentSongAsync(SingleSongBase song, CancellationToken ct)
    {
        _state.SetNowPlaying(song);
        _state.Duration = TimeSpan.FromMilliseconds(Math.Max(0, song.Duration));
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _lyricCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await _playCoreNotificationHub.PublishNotificationAsync(
            new CurrentSongChangedNotification { CurrentPlayingSong = song },
            ct);
    }

    #endregion

    #region Player Event Handlers

    /// <summary>
    ///     播放位置更新回调
    /// </summary>
    private void OnPositionChanged(TimeSpan position)
    {
        _state.Position = position;
        _lyricService.Tick(position);
        if (_currentSource is { } source)
        {
            var generation = _playbackGeneration;
            _taskRunner.Forget(
                HandleTransitionPositionAsync(source, generation, position),
                "update track transition");
        }
    }

    /// <summary>
    ///     全局播放状态变化回调
    /// </summary>
    private void OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        var playing = status == PlaybackStatus.Playing;
        _taskRunner.Forget(_uiThreadDispatcher.TryRunAsync(() => { _state.IsPlaying = playing; }),
            "publish playback status changed");
    }

    /// <summary>
    ///     曲目自然播放结束回调
    /// </summary>
    private void OnTrackReachesEnd(IPlaybackSource source)
    {
        if (!ReferenceEquals(source, _currentSource))
            return;

        _taskRunner.Forget(
            HandleTrackEndedSafeAsync(source, _playbackGeneration),
            "handle track ended");
    }

    private void OnPlaybackStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlaybackStateService.QueueRevision))
            return;

        _taskRunner.Forget(
            CancelTransitionForQueueChangeAsync(),
            "cancel transition after queue change");
    }

    private async Task CancelTransitionForQueueChangeAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <summary>
    ///     主播放源切换回调
    /// </summary>
    private void OnPrimaryPlaybackSourceChanged(IPlaybackSource source)
    {
        if (source is AudioGraphPlaybackSource agSource)
        {
            if (_state.NowPlayingProviderItem is null) return;

            _state.Duration = agSource.PlaybackSource.Duration ?? TimeSpan.Zero;
            _lyricCts?.Cancel();
            _lyricCts?.Dispose();
            _lyricCts = new CancellationTokenSource();
            _taskRunner.Forget(LoadLyricsSafeAsync(_state.NowPlayingProviderItem, _lyricCts.Token),
                "load lyrics for primary source");

            if (_state.NowPlayingProviderItem is not null)
                _taskRunner.Forget(_playbackNotification.OnTrackChangedAsync(_state.NowPlayingProviderItem),
                    "update playback notification on track changed");
        }
    }

    private async Task LoadLyricsSafeAsync(SingleSongBase? providerItem, CancellationToken ct)
    {
        try
        {
            if (providerItem is not null)
                await _lyricService.LoadLyricsAsync(providerItem, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Load lyrics failed: {ex.Message}");
        }
    }

    private async Task HandleTransitionPositionAsync(
        IPlaybackSource source,
        long generation,
        TimeSpan position)
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrentGeneration(source, generation))
                return;

            await _activeTransition
                .OnPositionChangedAsync(
                    CreateTransitionContext(source, generation, position),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Track transition update failed: {ex}");
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task HandleTrackEndedSafeAsync(IPlaybackSource endedSource, long generation)
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrentGeneration(endedSource, generation)
                || _state.NowPlayingProviderItem is null
                || _playCore.ActivePlayModeId == "ltg")
                return;

            if (_lastFmSettings.LastFMScrobble
                && _lastScrobbledGeneration != generation)
            {
                _lastScrobbledGeneration = generation;
                _taskRunner.Forget(
                    ScrobbleSafeAsync(_state.NowPlayingProviderItem),
                    "scrobble naturally completed track");
            }

            await _activeTransition
                .OnTrackCompletedAsync(
                    CreateTransitionContext(endedSource, generation, _state.Position),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Track end handling failed: {ex}");
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task LoadAndPlayCoreAsync(
        SingleSongBase song,
        bool autoPlay,
        bool removeCurrentSongs,
        CancellationToken ct)
    {
        if (removeCurrentSongs)
            await StopCoreAsync(ct).ConfigureAwait(false);

        await SetCurrentSongAsync(song, ct).ConfigureAwait(false);
        if (!autoPlay)
            return;

        await _playCore.PlayAsync(ct).ConfigureAwait(false);
        _state.IsPlaying = _playCore.CurrentPlayingTicket is not null;
        CaptureCurrentPlaybackIdentity();
    }

    private bool IsCurrentGeneration(IPlaybackSource source, long generation)
    {
        return generation == _playbackGeneration
               && ReferenceEquals(source, _currentSource)
               && ReferenceEquals(source, _player.PrimaryPlaybackSource);
    }

    private void CaptureCurrentPlaybackIdentity()
    {
        _currentSource = _playCore.CurrentPlayingTicket is ChopinAudioTicket ticket
            ? ticket.PlaybackSource
            : _player.PrimaryPlaybackSource;
        _playbackGeneration++;
    }

    private TrackTransitionContext CreateTransitionContext(
        IPlaybackSource source,
        long generation,
        TimeSpan position)
    {
        var duration = _player is AudioGraphPlayer { PrimaryAudioInputNode.Duration: { } actualDuration }
            ? actualDuration
            : TimeSpan.Zero;
        return new TrackTransitionContext
        {
            Host = new TransitionHost(this),
            Source = source,
            Generation = generation,
            Position = position,
            Duration = duration,
            CanPreload = _playCore.ActivePlayModeId is not ("sgl" or "ltg")
                         && duration >= TimeSpan.FromSeconds(30),
            PlaybackRate = _player.GetPlaybackSourceSpeed(source),
            CrossFadeDuration = TimeSpan.FromSeconds(Math.Clamp(_playbackSettings.CrossFadeTime, 3d, 10d))
        };
    }

    public Task HandleNotificationAsync(PlaybackRequestFailedNotification notification, CancellationToken ctk = new())
    {
        if (!IsCurrentPlaybackFailure(notification.Song))
            return Task.CompletedTask;

        _taskRunner.Forget(AutoSkipPlaybackFailureAsync(notification), "auto skip failed playback request");
        return Task.CompletedTask;
    }

    private async Task AutoSkipPlaybackFailureAsync(PlaybackRequestFailedNotification notification)
    {
        if (!await _autoSkipLock.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (!IsCurrentPlaybackFailure(notification.Song))
                return;

            var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
            if (queue.Count == 0)
            {
                _state.IsPlaying = false;
                return;
            }

            var failuresObserved = 1;
            while (failuresObserved < queue.Count)
            {
                await MoveNextAndPlayAsync(false, false).ConfigureAwait(false);

                if (_playCore.CurrentPlayingTicket is not null)
                    return;

                failuresObserved++;
                queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
                if (queue.Count == 0)
                {
                    _state.IsPlaying = false;
                    return;
                }
            }

            _state.IsPlaying = false;
            Debug.WriteLine("Playback auto-skip stopped because every queued song failed to resolve.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playback failure auto-skip failed: {ex.Message}");
        }
        finally
        {
            _autoSkipLock.Release();
        }
    }

    private bool IsCurrentPlaybackFailure(SingleSongBase? failedSong)
    {
        if (failedSong is null || _state.NowPlayingProviderItem is not { } currentSong)
            return false;

        return ReferenceEquals(failedSong, currentSong)
               || (failedSong.ProviderId == currentSong.ProviderId
                   && failedSong.TypeId == currentSong.TypeId
                   && failedSong.ActualId == currentSong.ActualId);
    }

    #endregion

    #region IDisposable

    /// <summary>
    ///     释放资源，取消订阅播放器事件
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_player is AudioGraphPlayer graphPlayer)
        {
            graphPlayer.OnTrackReachesEnd -= OnTrackReachesEnd;
            graphPlayer.OnGlobalPlaybackStatusChanged -= OnGlobalPlaybackStatusChanged;
            graphPlayer.OnPositionChanged -= OnPositionChanged;
            graphPlayer.OnPrimaryPlaybackSourceChanged -= OnPrimaryPlaybackSourceChanged;
        }

        _state.PropertyChanged -= OnPlaybackStatePropertyChanged;
        _smtc?.ButtonPressed -= SMTC_ButtonPressed;
        _smtc?.PlaybackPositionChangeRequested -= SMTC_PlaybackPositionChangeRequested;

        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _activeTransition.CancelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }

        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _autoSkipLock.Dispose();
        _transitionGate.Dispose();
        _initializeGate.Dispose();
    }

    #endregion
}
