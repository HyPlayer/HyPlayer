using AsyncAwaitBestPractices;
using Depository.Abstraction.Interfaces;
using Depository.Abstraction.Interfaces.NotificationHub;
using HyPlayer;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.LastFM;
using HyPlayer.Services.Playback.AudioServices;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 播放控制服务 — 封装底层 <see cref="IPlayer"/> 操作，协调播放状态更新。
/// <para>
/// 通过 <see cref="PlaybackStateService"/> 写入播放状态，并通过 owner service events 发布业务事件。
/// </para>
/// </summary>
public sealed partial class PlaybackControlService : IPlaybackControlService,
                                                     INotificationSubscriber<PlaybackRequestFailedNotification>,
                                                     IDisposable
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
    private SystemMediaTransportControls? _smtc;

    private readonly SemaphoreSlim _seekerLock = new(1, 1);
    private readonly SemaphoreSlim _autoSkipLock = new(1, 1);
    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _lyricCts;
    private bool _disposed;
    private bool _initialized;

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
        ChopinAudioService audioService)
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
    }

    #region IPlaybackControlService

    /// <inheritdoc />
    public bool IsPlaying => _state.IsPlaying;

    /// <inheritdoc />
    public TimeSpan Position => _state.Position;

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
        if (_initialized) return;
        _initialized = true;

        if (!(_player is AudioGraphPlayer agp && agp.PlayerCreated))
        {
            await _player.InitializePlayer(new AudioGraphAudioSetting
            {
                DefaultDeviceId = _setting.AudioRenderDevice,
                OutputVolume = _setting.Volume / 100d,
                AutoFallback = true,
                EnableFFTProcessing = _setting.EnableFFT
            });
        }

        // SMTC 需要在 UI 线程获取，由调用方保证
        if (_player is AudioGraphPlayer graphPlayer)
        {
            var smtc = Windows.Media.SystemMediaTransportControls.GetForCurrentView();
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.IsEnabled = true;
            smtc.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Music;
            smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Closed;
            graphPlayer.SMTCManager = new UWP.Chopin.SMTCManager(smtc);
            _smtc = smtc;

            // 订阅播放器事件
            graphPlayer.OnTrackReachesEnd += OnTrackReachesEnd;
            graphPlayer.OnGlobalPlaybackStatusChanged += OnGlobalPlaybackStatusChanged;
            graphPlayer.OnPositionChanged += OnPositionChanged;
            graphPlayer.OnPrimaryPlaybackSourceChanged += OnPrimaryPlaybackSourceChanged;
            smtc.ButtonPressed += SMTC_ButtonPressed;
            smtc.PlaybackPositionChangeRequested += SMTC_PlaybackPositionChangeRequested;
        }

        _state.Volume = _setting.Volume / 100d;
    }

    private void SMTC_PlaybackPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        SeekAsync(args.RequestedPlaybackPosition).SafeFireAndForget();
    }

    private void SMTC_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                Play();
                break;

            case SystemMediaTransportControlsButton.Pause:
                Pause();
                break;

            case SystemMediaTransportControlsButton.Next:
                _taskRunner.Forget(MoveNextAndPlayAsync(userInitiated: true), "SMTC next");
                break;

            case SystemMediaTransportControlsButton.Previous:
                _taskRunner.Forget(MovePreviousAndPlayAsync(), "SMTC previous");
                break;
        }
    }

    /// <inheritdoc />
    public void Play()
    {
        _taskRunner.Forget(PlayCoreAfterInitializationAsync(), "play via PlayCore");
        _state.IsPlaying = true;
    }

    private async Task PlayCoreAfterInitializationAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        await _playCore.PlayAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Pause()
    {
        _taskRunner.Forget(_playCore.PauseAsync(), "pause via PlayCore");
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
        try
        {
            await _seekerLock.WaitAsync();

            await _playCore.SeekAsync((long)target.TotalMilliseconds);

            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(target));

            // 与原始实现一致，等待 seek 稳定
            await Task.Delay(500);
        }
        finally
        {
            _seekerLock.Release();
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

            if (removeCurrentSongs)
                await _playCore.StopAsync(ct);

            await SetCurrentSongAsync(song, ct);
            if (autoPlay)
            {
                await _playCore.PlayAsync(ct);
                _state.IsPlaying = _playCore.CurrentPlayingTicket is not null;
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
        if (position >= _setting.ABEndPoint && _setting.ABEndPoint != TimeSpan.Zero &&
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
        if (!ReferenceEquals(source, _player.PrimaryPlaybackSource)) return;

        if (_state.NowPlayingProviderItem is null) return;

        if (_setting.LastFMScrobble && _state.NowPlayingProviderItem is not null)
        {
            _taskRunner.Forget(_playbackNotification.ScrobbleAsync(_state.NowPlayingProviderItem), "update Last.FM scrobble");
        }
        _taskRunner.Forget(HandleTrackEndedSafeAsync(), "handle track ended");
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

    private async Task HandleTrackEndedSafeAsync()
    {
        try
        {
            if (_playCore.ActivePlayModeId == "ltg")
                return;

            await MoveNextAndPlayAsync(userInitiated: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Track end handling failed: {ex.Message}");
        }
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
            await SeekAsync(TimeSpan.Zero).ConfigureAwait(false);
            Play();
            return;
        }
        else
        {
            await _playCore.MoveNextAsync().ConfigureAwait(false);
        }

        if (_playCore.CurrentSong is { } song)
            await LoadAndPlayAsync(song, removeCurrentSongs: false).ConfigureAwait(false);
    }

    public async Task MovePreviousAndPlayAsync()
    {
        if ((await _playCore.GetPlaylistAsync().ConfigureAwait(false)).Count == 0)
            return;

        await _playCore.MovePreviousAsync().ConfigureAwait(false);
        if (_playCore.CurrentSong is { } song)
            await LoadAndPlayAsync(song, removeCurrentSongs: false).ConfigureAwait(false);
    }

    #region IDisposable

    /// <summary>
    /// 释放资源，取消订阅播放器事件
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_player is AudioGraphPlayer graphPlayer)
        {
            graphPlayer.OnTrackReachesEnd -= OnTrackReachesEnd;
            graphPlayer.OnGlobalPlaybackStatusChanged -= OnGlobalPlaybackStatusChanged;
            graphPlayer.OnPositionChanged -= OnPositionChanged;
            graphPlayer.OnPrimaryPlaybackSourceChanged -= OnPrimaryPlaybackSourceChanged;
        }
        _smtc?.ButtonPressed -= SMTC_ButtonPressed;
        _smtc?.PlaybackPositionChangeRequested -= SMTC_PlaybackPositionChangeRequested;
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _seekerLock.Dispose();
        _autoSkipLock.Dispose();
    }

    #endregion
}
