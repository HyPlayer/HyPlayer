using AsyncAwaitBestPractices;
using Depository.Abstraction.Interfaces;
using Depository.Abstraction.Interfaces.NotificationHub;
using HyPlayer;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Playback.Transitions;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
/// 播放控制服务 — 封装底层 <see cref="IPlayer"/> 操作，协调播放状态更新。
/// <para>
/// 通过 <see cref="PlaybackStateService"/> 写入播放状态，并通过 owner service events 发布业务事件。
/// </para>
/// </summary>
public sealed partial class PlaybackControlService : IPlaybackControlService,
                                                     INotificationSubscriber<PlaybackRequestFailedNotification>,
                                                     IDisposable,
                                                     IAsyncDisposable
{
    public event EventHandler<SeekRequestedEventArgs>? SeekRequested;

    private readonly IPlayer _player;
    private readonly PlayCoreBase _playCore;
    private readonly INotificationHub _playCoreNotificationHub;
    private readonly PlaybackStateService _state;
    private readonly Setting _setting;
    private readonly ILyricService _lyricService;
    private readonly INotificationService _notification;
    private readonly IPlaybackNotificationService _playbackNotification;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IReadOnlyList<IMusicResourceProvidable> _musicResourceProviders;
    private readonly ChopinAudioService _audioService;
    private readonly SmtcPlaybackCommandDispatcher _smtcCommandDispatcher;
    private SystemMediaTransportControls? _smtc;

    private readonly SemaphoreSlim _autoSkipLock = new(1, 1);
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly IReadOnlyDictionary<string, ITrackTransition> _transitions;
    private ITrackTransition _activeTransition;
    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _lyricCts;
    private IPlaybackSource? _currentSource;
    private long _playbackGeneration;
    private long _lastScrobbledGeneration = -1;
    private bool _disposed;
    private bool _initialized;
    private bool _smtcSubscribed;

    /// <summary>
    /// 创建 <see cref="PlaybackControlService"/> 实例。
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
        Setting setting,
        ILyricService lyricService,
        INotificationService notification,
        IPlaybackNotificationService playbackNotification,
        IBackgroundTaskRunner taskRunner,
        IEnumerable<IMusicResourceProvidable> musicResourceProviders,
        ChopinAudioService audioService,
        IEnumerable<ITrackTransition> transitions)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _playCore = playCore ?? throw new ArgumentNullException(nameof(playCore));
        _playCoreNotificationHub = playCoreNotificationHub ?? throw new ArgumentNullException(nameof(playCoreNotificationHub));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _setting = setting ?? throw new ArgumentNullException(nameof(setting));
        _lyricService = lyricService ?? throw new ArgumentNullException(nameof(lyricService));
        _notification = notification ?? throw new ArgumentNullException(nameof(notification));
        _playbackNotification = playbackNotification ?? throw new ArgumentNullException(nameof(playbackNotification));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
        _musicResourceProviders = musicResourceProviders?.ToList() ?? throw new ArgumentNullException(nameof(musicResourceProviders));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _transitions = transitions?.ToDictionary(transition => transition.Id)
                       ?? throw new ArgumentNullException(nameof(transitions));
        _activeTransition = _transitions.TryGetValue(_setting.TransitionId, out var transition)
            ? transition
            : _transitions["dir"];
        _smtcCommandDispatcher = new SmtcPlaybackCommandDispatcher(
            PlayCoreAfterInitializationAsync,
            PauseCoreAsync,
            () => MoveNextAndPlayAsync(userInitiated: true),
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

    /// <inheritdoc />
    public double Volume
    {
        get => _state.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            _player.SetOutputVolume(clamped);
            _setting.Volume = (int)(clamped * 100);
            _state.Volume = clamped;
        }
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
                DefaultDeviceId = _setting.AudioRenderDevice,
                OutputVolume = _setting.Volume / 100d,
                AutoFallback = true,
                EnableFFTProcessing = _setting.EnableFFT
            }).ConfigureAwait(false);

            if (_player is AudioGraphPlayer graphPlayer)
            {
                SystemMediaTransportControls? smtc = null;
                var uiInitialization = _notification.InvokeOnUIThread(() =>
                {
                    smtc = Windows.Media.SystemMediaTransportControls.GetForCurrentView();
                    smtc.IsPlayEnabled = true;
                    smtc.IsPauseEnabled = true;
                    smtc.IsNextEnabled = true;
                    smtc.IsPreviousEnabled = true;
                    smtc.IsEnabled = true;
                    smtc.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Music;
                    smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Closed;
                    graphPlayer.SMTCManager = new UWP.Chopin.SMTCManager(smtc);
                    _smtc = smtc;

                    if (!_smtcSubscribed)
                    {
                        smtc.ButtonPressed += SMTC_ButtonPressed;
                        smtc.PlaybackPositionChangeRequested += SMTC_PlaybackPositionChangeRequested;
                        _smtcSubscribed = true;
                    }
                });
                if (uiInitialization is null)
                    throw new InvalidOperationException("SMTC initialization requires an active UI view.");

                await uiInitialization;
                if (smtc is null)
                    throw new InvalidOperationException("SMTC initialization did not return a control instance.");
            }

            _state.Volume = _setting.Volume / 100d;
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private void SMTC_PlaybackPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        SeekAsync(args.RequestedPlaybackPosition).SafeFireAndForget();
    }

    private void SMTC_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
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
        await _playCore.PlayAsync().ConfigureAwait(false);

        if (_playCore.CurrentPlayingTicket is null)
            _state.IsPlaying = false;
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

    /// <inheritdoc />
    public void CheckABTimeRemaining(TimeSpan position)
    {
        if (_setting.ABRepeatStatus
            && position >= _setting.ABEndPoint && _setting.ABEndPoint != TimeSpan.Zero &&
            _setting.ABEndPoint > _setting.ABStartPoint)
            _taskRunner.Forget(SeekAsync(_setting.ABStartPoint), "seek to AB repeat start");
    }

    #endregion

    #region Player Event Handlers

    /// <summary>
    /// 播放位置更新回调
    /// </summary>
    private void OnPositionChanged(TimeSpan position)
    {
        _state.Position = position;
        _lyricService.Tick(position);
        CheckABTimeRemaining(position);
        if (_currentSource is { } source)
        {
            var generation = _playbackGeneration;
            _taskRunner.Forget(
                HandleTransitionPositionAsync(source, generation, position),
                "update track transition");
        }
    }

    /// <summary>
    /// 全局播放状态变化回调
    /// </summary>
    private void OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        var playing = status == PlaybackStatus.Playing;
        _taskRunner.Forget(_notification.InvokeOnUIThread(() =>
        {
            _state.IsPlaying = playing;
        }), "publish playback status changed");
    }

    /// <summary>
    /// 曲目自然播放结束回调
    /// </summary>
    private void OnTrackReachesEnd(IPlaybackSource source)
    {
        if (!ReferenceEquals(source, _currentSource))
            return;

        _taskRunner.Forget(
            HandleTrackEndedSafeAsync(source, _playbackGeneration),
            "handle track ended");
    }

    private void OnPlaybackStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
    /// 主播放源切换回调
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
            _taskRunner.Forget(LoadLyricsSafeAsync(_state.NowPlayingProviderItem, _lyricCts.Token), "load lyrics for primary source");

            if (_state.NowPlayingProviderItem is not null)
                _taskRunner.Forget(_playbackNotification.OnTrackChangedAsync(_state.NowPlayingProviderItem), "update playback notification on track changed");
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
            System.Diagnostics.Debug.WriteLine($"Load lyrics failed: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Track transition update failed: {ex}");
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

            if (_setting.LastFMScrobble
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
            System.Diagnostics.Debug.WriteLine($"Track end handling failed: {ex}");
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

    private bool IsCurrentGeneration(IPlaybackSource source, long generation) =>
        generation == _playbackGeneration
        && ReferenceEquals(source, _currentSource)
        && ReferenceEquals(source, _player.PrimaryPlaybackSource);

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
        var hasAbLoop = _setting.ABRepeatStatus
                        && _setting.ABEndPoint > _setting.ABStartPoint
                        && _setting.ABEndPoint != TimeSpan.Zero;

        return new TrackTransitionContext
        {
            Host = new TransitionHost(this),
            Source = source,
            Generation = generation,
            Position = position,
            Duration = duration,
            CanPreload = !hasAbLoop
                         && _playCore.ActivePlayModeId is not ("sgl" or "ltg")
                         && duration >= TimeSpan.FromSeconds(30),
            HasActiveAbLoop = hasAbLoop,
            PlaybackRate = _player.GetPlaybackSourceSpeed(source),
            CrossFadeDuration = TimeSpan.FromSeconds(Math.Clamp(_setting.CrossFadeTime, 3d, 10d))
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
                await MoveNextAndPlayAsync(userInitiated: false, repeatSingleOnAutoAdvance: false).ConfigureAwait(false);

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
            System.Diagnostics.Debug.WriteLine("Playback auto-skip stopped because every queued song failed to resolve.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Playback failure auto-skip failed: {ex.Message}");
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

    public async Task MoveNextAndPlayAsync(bool userInitiated)
        => await MoveNextAndPlayAsync(userInitiated, repeatSingleOnAutoAdvance: true).ConfigureAwait(false);

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

        if (_setting.LastFMScrobble
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
            System.Diagnostics.Debug.WriteLine($"Last.fm scrobble failed: {ex}");
        }
    }

    private static bool SameSong(SingleSongBase? left, SingleSongBase? right) =>
        ReferenceEquals(left, right)
        || (left is not null
            && right is not null
            && left.ProviderId == right.ProviderId
            && left.TypeId == right.TypeId
            && left.ActualId == right.ActualId);

    private sealed class TransitionHost : ITrackTransitionHost
    {
        private readonly PlaybackControlService _owner;

        public TransitionHost(PlaybackControlService owner)
        {
            _owner = owner;
        }

        public Task<TransitionPreparedTrack?> PrepareNextAsync(
            TrackTransitionContext context,
            CancellationToken ct) =>
            _owner.PrepareNextCoreAsync(context, ct);

        public Task<PreparedPlaybackPromotion?> PromoteAsync(
            TransitionPreparedTrack prepared,
            CancellationToken ct) =>
            _owner.PromoteCoreAsync(prepared, ct);

        public Task AdvanceDirectAsync(TrackTransitionContext context, CancellationToken ct)
        {
            if (!_owner.IsCurrentGeneration(context.Source, context.Generation))
                return Task.CompletedTask;

            return _owner.MoveNextAndPlayCoreAsync(
                userInitiated: false,
                repeatSingleOnAutoAdvance: true,
                ct);
        }
    }

    #region IDisposable

    /// <summary>
    /// 释放资源，取消订阅播放器事件
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
