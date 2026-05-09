using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 播放控制服务 — 封装底层 <see cref="IPlayer"/> 操作，协调播放状态更新。
/// <para>
/// 通过 <see cref="WeakReferenceMessenger"/> 发送事件消息，避免与 PlaylistService 产生循环依赖：
/// <list type="bullet">
///   <item><see cref="TrackEndedMessage"/> — 曲目自然播放结束</item>
///   <item><see cref="TrackChangedMessage"/> — 当前播放曲目切换</item>
///   <item><see cref="PlaybackStateChangedMessage"/> — 播放/暂停状态变化</item>
///   <item><see cref="PositionTickMessage"/> — 播放位置更新</item>
///   <item><see cref="SeekRequestedMessage"/> — 用户手动拖动进度条</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class PlaybackControlService : IPlaybackControlService, IDisposable
{
    private readonly IPlayer _player;
    private readonly IMediaSourceService _mediaSourceService;
    private readonly PlaybackStateService _state;
    private readonly Setting _setting;
    private readonly ILyricService _lyricService;
    private readonly INotificationService _notification;
    private IPlaylistService? _playlistService;

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
        INotificationService notification)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _mediaSourceService = mediaSourceService ?? throw new ArgumentNullException(nameof(mediaSourceService));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _setting = setting ?? throw new ArgumentNullException(nameof(setting));
        _lyricService = lyricService ?? throw new ArgumentNullException(nameof(lyricService));
        _notification = notification ?? throw new ArgumentNullException(nameof(notification));
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
            graphPlayer.SMTCManager = new HyPlayer.UWP.Chopin.SMTCManager(smtc);

            // 订阅播放器事件
            graphPlayer.OnTrackReachesEnd += OnTrackReachesEnd;
            graphPlayer.OnGlobalPlaybackStatusChanged += OnGlobalPlaybackStatusChanged;
            graphPlayer.OnPositionChanged += OnPositionChanged;
            graphPlayer.OnPrimaryPlaybackSourceChanged += OnPrimaryPlaybackSourceChanged;
        }

        _state.Volume = _setting.Volume / 100d;
    }

    /// <inheritdoc />
    public void Play()
    {
        _player.PlayAll();
        if(_player.PrimaryPlaybackSource.PlaybackStatus is not PlaybackStatus.Playing)
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

            WeakReferenceMessenger.Default.Send(new SeekRequestedMessage(target));

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
                _player.RemoveAllPlaybackSource();

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

            if (setAsPrimary)
            {
                _ = _notification.InvokeOnUIThread(() =>
                {
                    _state.NowPlayingItem = item;
                    _state.Duration = TimeSpan.FromMilliseconds(item.LengthInMilliseconds);
                    _state.IsPlaying = autoPlay;
                });

                _lyricCts?.Cancel();
                _lyricCts?.Dispose();
                _lyricCts = new CancellationTokenSource();
                _ = LoadLyricsSafeAsync(item, _lyricCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 加载被取消，静默忽略
        }
    }

    /// <inheritdoc />
    public void CheckABTimeRemaining(TimeSpan position)
    {
        if (position >= _setting.ABEndPoint && _setting.ABEndPoint != TimeSpan.Zero &&
            _setting.ABEndPoint > _setting.ABStartPoint)
            _ = SeekAsync(_setting.ABStartPoint);
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
        (_playlistService ??= Ioc.Default.GetRequiredService<IPlaylistService>()).OnPositionTick(position, _state.Duration);
        WeakReferenceMessenger.Default.Send(new PositionTickMessage(position));
    }

    /// <summary>
    /// 全局播放状态变化回调
    /// </summary>
    private void OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        var playing = status == PlaybackStatus.Playing;
        _ = _notification.InvokeOnUIThread(() =>
        {
            _state.IsPlaying = playing;
            WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(playing));
        });
    }

    /// <summary>
    /// 曲目自然播放结束回调
    /// </summary>
    private void OnTrackReachesEnd(IPlaybackSource source)
    {
        var agSource = source as AudioGraphPlaybackSource;
        var item = agSource?.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
        if (item is null) return;

        WeakReferenceMessenger.Default.Send(new TrackEndedMessage(item!));
        if (!_state.IsInFm)
        {
            _ = HandleTrackEndedSafeAsync();
        }
    }

    /// <summary>
    /// 主播放源切换回调
    /// </summary>
    private void OnPrimaryPlaybackSourceChanged(IPlaybackSource source)
    {
        if (source is AudioGraphPlaybackSource agSource
            && agSource.PlaybackSource?.CustomProperties.TryGetValue("nowPlayingItem", out var obj) == true
            && obj is HyPlayItem item)
        {
            _ = _notification.InvokeOnUIThread(() =>
            {
                _state.NowPlayingItem = item;
                _state.Duration = TimeSpan.FromMilliseconds(item.LengthInMilliseconds);
            });

            _lyricCts?.Cancel();
            _lyricCts?.Dispose();
            _lyricCts = new CancellationTokenSource();
            _ = LoadLyricsSafeAsync(item, _lyricCts.Token);

            _ = Ioc.Default.GetRequiredService<IPlaybackNotificationService>().OnTrackChangedAsync(item);
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
            if (_state.NowPlayingItem is not null)
            {
                await Ioc.Default.GetRequiredService<IPlaybackNotificationService>().ScrobbleAsync(_state.NowPlayingItem);
            }

            await (_playlistService ??= Ioc.Default.GetRequiredService<IPlaylistService>()).OnTrackEndedAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Track end handling failed: {ex.Message}");
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

        _mediaSourceCts?.Cancel();
        _mediaSourceCts?.Dispose();
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _seekerLock.Dispose();
    }

    #endregion
}
