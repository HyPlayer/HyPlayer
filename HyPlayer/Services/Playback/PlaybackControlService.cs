using AsyncAwaitBestPractices;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.LastFM;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using PlayItem = HyPlayer.Domain.Music.PlayItem;

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
    private readonly IMediaSourceService _mediaSourceService;
    private readonly PlaybackStateService _state;
    private readonly Setting _setting;
    private readonly ILyricService _lyricService;
    private readonly ITeachingTipService _teachingTipService;
    private readonly IPlaybackNotificationService _playbackNotification;
    private readonly IBackgroundTaskRunner _taskRunner;
    private SystemMediaTransportControls? _smtc;

    private readonly SemaphoreSlim _seekerLock = new(1, 1);
    private CancellationTokenSource? _mediaSourceCts;
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
        IMediaSourceService mediaSourceService,
        PlaybackStateService state,
        Setting setting,
        ILyricService lyricService,
        ITeachingTipService teachingTipService,
        IPlaybackNotificationService playbackNotification,
        IBackgroundTaskRunner taskRunner)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _mediaSourceService = mediaSourceService ?? throw new ArgumentNullException(nameof(mediaSourceService));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _setting = setting ?? throw new ArgumentNullException(nameof(setting));
        _lyricService = lyricService ?? throw new ArgumentNullException(nameof(lyricService));
        _teachingTipService = teachingTipService ?? throw new ArgumentNullException(nameof(teachingTipService));
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
            var smtc = SystemMediaTransportControls.GetForCurrentView();
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.IsEnabled = true;
            smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
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
        }
    }

    /// <inheritdoc />
    public void Play()
    {
        _player.PlayAll();
        if (_player.PrimaryPlaybackSource.PlaybackStatus is not PlaybackStatus.Playing)
        {
            _player.PlayPlaybackSource(_player.PrimaryPlaybackSource);
        }
        _state.IsPlaying = true;
    }

    /// <inheritdoc />
    public void Pause()
    {
        _player.PauseAll();
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

            if (_player is AudioGraphPlayer agp && agp.PrimaryPlaybackSource is null)
                return;

            if (_player is AudioGraphPlayer graphPlayer)
                _player.SeekPlaybackSource(target, graphPlayer.PrimaryPlaybackSource);

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
        // 取消上一次加载
        _mediaSourceCts?.Cancel();
        _mediaSourceCts?.Dispose();
        _mediaSourceCts = new CancellationTokenSource();
        var ct = _mediaSourceCts.Token;

        try
        {
            if (removeCurrentSongs)
            {
                var oldItem = _state.NowPlayingItem;
                _player.RemoveAllPlaybackSource();
                oldItem?.PlayItem?.Dispose();
                oldItem?.PlayItem = null;
            }
            item.PlayItem?.Dispose();
            item.PlayItem = null;

            var mediaSource = await _mediaSourceService.CreateMediaSourceAsync(item, ct);
            if (mediaSource is null) return;

            ct.ThrowIfCancellationRequested();
            item.PlayItem ??= new PlayItem();
            mediaSource.CustomProperties["nowPlayingItem"] = item;

            var playbackSource = new AudioGraphPlaybackSource(mediaSource);
            item.PlayItem.AudioGraphPlaybackSource = playbackSource;

            var targetVolume = _setting.EnableAudioGain ? (item.Volume ?? 1d) : 1d;
            var options = new PlaybackOptions
            {
                SetAsPrimarySource = setAsPrimary,
                AutoPlay = autoPlay,
                Volume = targetVolume
            };

            await _player.ConnectPlaybackSourceAsync(playbackSource, options);
        }
        catch (OperationCanceledException)
        {
            // 加载被取消，静默忽略
        }
    }

    #endregion

    #region Player Event Handlers

    /// <summary>
    /// 播放位置更新回调
    /// </summary>
    private void OnPositionChanged(object sender, TimeSpan position)
    {
        _state.Position = position;
        _lyricService.Tick(position);
    }

    /// <summary>
    /// 全局播放状态变化回调
    /// </summary>
    private void OnGlobalPlaybackStatusChanged(object sender, PlaybackStatus status)
    {
        var playing = status == PlaybackStatus.Playing;
        _state.IsPlaying = playing;
    }

    /// <summary>
    /// 曲目自然播放结束回调
    /// </summary>
    private void OnTrackReachesEnd(object sender, IPlaybackSource source)
    {
        if (!ReferenceEquals(source, _player.PrimaryPlaybackSource)) return;

        var agSource = source as AudioGraphPlaybackSource;
        var item = agSource?.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
        if (item is null) return;

        if (_setting.LastFMScrobble)
        {
            _taskRunner.Forget(LastFMManager.Scrobble(item), "update Last.FM now playing");
        }
    }

    /// <summary>
    /// 主播放源切换回调
    /// </summary>
    private void OnPrimaryPlaybackSourceChanged(object sender, IPlaybackSource source)
    {
        if (source is AudioGraphPlaybackSource agSource
            && agSource.PlaybackSource?.CustomProperties.TryGetValue("nowPlayingItem", out var obj) == true
            && obj is HyPlayItem item)
        {
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

    #endregion

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
        _mediaSourceCts?.Cancel();
        _mediaSourceCts?.Dispose();
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _seekerLock.Dispose();
    }

    #endregion
}
