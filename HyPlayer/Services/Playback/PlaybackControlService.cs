using AsyncAwaitBestPractices;
using Depository.Abstraction.Interfaces;
using HyPlayer;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.LastFM;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
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
public sealed partial class PlaybackControlService : IPlaybackControlService, IDisposable
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
    private bool _resolvingPlaylistService;
    private IPlaylistService? _playlistService;
    private SystemMediaTransportControls? _smtc;

    private readonly SemaphoreSlim _seekerLock = new(1, 1);
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
        IBackgroundTaskRunner taskRunner)
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
                _playlistService.MoveNextAsync();
                break;

            case SystemMediaTransportControlsButton.Previous:
                _playlistService.MovePreviousAsync();
                break;
        }
    }

    /// <inheritdoc />
    public void Play()
    {
        _taskRunner.Forget(_playCore.PlayAsync(), "play via PlayCore");
        _state.IsPlaying = true;
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
    public async Task LoadAndPlayAsync(HyPlayItem item, bool setAsPrimary = true, bool autoPlay = true, bool removeCurrentSongs = true)
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();
        var ct = _playbackCts.Token;

        try
        {
            var song = item.ToSingleSong();
            if (song is null) return;

            if (removeCurrentSongs)
                await _playCore.StopAsync(ct);

            await SetCurrentSongAsync(item, song, ct);
            if (autoPlay)
                await _playCore.PlayAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SetCurrentSongAsync(HyPlayItem item, SingleSongBase song, CancellationToken ct)
    {
        _state.NowPlayingItem = item;
        _state.Duration = TimeSpan.FromMilliseconds(Math.Max(0, item.LengthInMilliseconds));
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _lyricCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await _playCoreNotificationHub.PublishNotificationAsync(
            new CurrentSongChangedNotification { CurrentPlayingSong = song },
            ct);

        _taskRunner.Forget(LoadLyricsSafeAsync(item, _lyricCts.Token), "load lyrics for PlayCore current song");
        _taskRunner.Forget(_playbackNotification.OnTrackChangedAsync(item), "update playback notification on PlayCore current song");
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
        GetPlaylistService()?.OnPositionTick(position, _state.Duration);
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

        var item = _state.NowPlayingItem;
        if (item is null) return;

        if (_setting.LastFMScrobble)
        {
            _taskRunner.Forget(LastFMManager.Scrobble(item), "update Last.FM now playing");
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
            var item = _state.NowPlayingItem;
            if (item is null) return;

            _state.Duration = agSource.PlaybackSource.Duration ?? TimeSpan.Zero;
            _lyricCts?.Cancel();
            _lyricCts?.Dispose();
            _lyricCts = new CancellationTokenSource();
            _taskRunner.Forget(LoadLyricsSafeAsync(item, _lyricCts.Token), "load lyrics for primary source");

            _taskRunner.Forget(_playbackNotification.OnTrackChangedAsync(item), "update playback notification on track changed");
        }
    }

    private async Task LoadLyricsSafeAsync(HyPlayItem item, CancellationToken ct)
    {
        try
        {
            await _lyricService.LoadLyricsAsync(item, ct);
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
            var playlistService = GetPlaylistService();
            if (playlistService is not null)
                await playlistService.OnTrackEndedAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Track end handling failed: {ex.Message}");
        }
    }

    #endregion

    private IPlaylistService? GetPlaylistService()
    {
        if (_playlistService is not null)
            return _playlistService;

        if (_resolvingPlaylistService)
            return null;

        try
        {
            _resolvingPlaylistService = true;
            _playlistService = AppDepository.Resolve<IPlaylistService>();
            return _playlistService;
        }
        finally
        {
            _resolvingPlaylistService = false;
        }
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
    }

    #endregion
}
