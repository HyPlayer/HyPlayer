using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Services.Playback.Transitions;

/// <summary>
/// 交叉淡入淡出过渡策略。
/// <para>
/// 在当前曲目即将结束时预加载下一首，并在剩余时间 ≤ <see cref="Setting.CrossFadeTime"/> 时
/// 同时对当前曲目执行淡出、对下一曲目执行淡入，实现平滑过渡。
/// </para>
/// </summary>
public sealed class CrossFadeTransition : ITrackTransition, IDisposable
{
    private readonly Setting _setting;

    private AudioGraphPlaybackSource? _currentPlaybackSource;
    private AudioGraphPlaybackSource? _nextPlaybackSource;
    private HyPlayItem? _nextItem;

    private double _currentInitialVolume = 1.0;
    private double _nextInitialVolume = 1.0;

    private readonly SemaphoreSlim _loaderSemaphore = new(1, 1);

    /// <summary>是否已预加载下一首</summary>
    private volatile bool _preloaded;

    /// <summary>是否正在执行淡入淡出</summary>
    private volatile bool _processing;

    private bool _disposed;

    /// <inheritdoc />
    public string Id => "xfd";

    /// <summary>
    /// 创建交叉淡入淡出过渡实例。
    /// </summary>
    /// <param name="setting">应用设置，提供 CrossFade / CrossFadeTime / EnableAudioGain 配置。</param>
    public CrossFadeTransition(Setting setting)
    {
        _setting = setting ?? throw new ArgumentNullException(nameof(setting));
    }

    /// <summary>
    /// 每次播放位置更新时调用。
    /// <list type="number">
    ///   <item>当剩余时间 ≤ max(总时长×0.125, 20s) 时触发预加载。</item>
    ///   <item>当剩余时间 ≤ CrossFadeTime 时开始淡入淡出处理。</item>
    /// </list>
    /// </summary>
    public void OnPositionTick(TrackTransitionContext ctx)
    {
        if (!_setting.CrossFade) return;

        // 尝试预加载
        _ = TryPreloadAsync(ctx);

        if (!_preloaded) return;

        var remaining = (ctx.Duration - ctx.Position).TotalSeconds;

        // 开始淡入淡出
        if (remaining <= _setting.CrossFadeTime && !_processing)
        {
            _processing = true;
        }

        if (!_processing) return;

        // 淡出当前曲目
        if (_currentPlaybackSource is not null)
        {
            ProcessFadeOut(ctx, remaining);
        }

        // 淡入下一曲目
        if (_nextPlaybackSource is not null)
        {
            ProcessFadeIn(ctx);
        }
    }

    /// <summary>
    /// 当前曲目自然结束时调用。
    /// 如果已预加载，则断开旧源并清理；否则回退到直接切歌。
    /// </summary>
    public async Task OnTrackEndedAsync(TrackTransitionContext ctx)
    {
        if (_currentPlaybackSource is not null)
        {
            // 已经在淡入淡出中或已完成预加载 — 断开旧源
            ctx.Player.DisconnectPlaybackSource(_currentPlaybackSource);
            HyPlayItem? oldItem = null;
            if (_currentPlaybackSource.PlaybackSource?.CustomProperties.TryGetValue("nowPlayingItem", out var obj) == true)
                oldItem = obj as HyPlayItem;
            oldItem?.PlayItem?.Dispose();
            if (oldItem is not null)
            {
                oldItem.PlayItem = null;
            }

            _currentPlaybackSource = null;
        }
        else if (_nextPlaybackSource is null)
        {
            // 未预加载 — 回退到直接切歌
            var nextItem = await ctx.RequestNextItemAsync(true).ConfigureAwait(false);
            if (nextItem is not null)
            {
                await ctx.LoadMediaSourceAsync(nextItem, true, true).ConfigureAwait(false);
            }
        }

        // 清理淡入淡出状态
        _processing = false;
        _preloaded = false;
    }

    /// <summary>
    /// 用户手动切歌时调用 — 取消正在进行的淡入淡出，断开预加载源，重置状态。
    /// </summary>
    public Task OnManualSkipAsync(TrackTransitionContext ctx)
    {
        if (!_processing && !_preloaded) return Task.CompletedTask;

        // 断开并清理当前源
        if (_currentPlaybackSource is not null)
        {
            ctx.Player.DisconnectPlaybackSource(_currentPlaybackSource);
            HyPlayItem? currentItem = null;
            if (_currentPlaybackSource.PlaybackSource?.CustomProperties.TryGetValue("nowPlayingItem", out var obj) == true)
                currentItem = obj as HyPlayItem;
            currentItem?.PlayItem?.Dispose();
            if (currentItem is not null)
            {
                currentItem.PlayItem = null;
            }
        }

        // 断开并清理预加载的下一首源
        if (_nextPlaybackSource is not null)
        {
            ctx.Player.DisconnectPlaybackSource(_nextPlaybackSource);
            _nextItem?.PlayItem?.Dispose();
            if (_nextItem is not null)
            {
                _nextItem.PlayItem = null;
            }
        }

        ResetInternal();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重置所有内部状态。
    /// </summary>
    public void Reset()
    {
        ResetInternal();
    }

    /// <summary>
    /// 尝试预加载下一首曲目。
    /// 当剩余时间 ≤ max(总时长×0.125, 20) 且总时长 &gt; 10s 时触发。
    /// </summary>
    private async Task TryPreloadAsync(TrackTransitionContext ctx)
    {
        if (_preloaded || _processing || !_setting.CrossFade) return;

        try
        {
            await _loaderSemaphore.WaitAsync().ConfigureAwait(false);
            if (_preloaded || _processing) return;

            var totalSeconds = ctx.Duration.TotalSeconds;
            var currentSeconds = ctx.Position.TotalSeconds;
            var remaining = totalSeconds - currentSeconds;
            var targetTime = Math.Max(totalSeconds * 0.125, 20);

            if (remaining > targetTime || totalSeconds <= 10) return;

            _preloaded = true;

            // 记录当前播放源
            if (ctx.CurrentItem?.PlayItem?.AudioGraphPlaybackSource is AudioGraphPlaybackSource currentSource)
            {
                _currentPlaybackSource = currentSource;
                _currentInitialVolume = ctx.CurrentItem.Volume ?? 1.0;
            }

            // 请求下一首
            var nextItem = await ctx.RequestNextItemAsync(true).ConfigureAwait(false);
            if (nextItem is null)
            {
                _preloaded = false;
                return;
            }

            _nextItem = nextItem;

            // 加载但不自动播放（play=false, setPrimary=false）
            await ctx.LoadMediaSourceAsync(nextItem, false, false).ConfigureAwait(false);

            if (nextItem.PlayItem?.AudioGraphPlaybackSource is AudioGraphPlaybackSource nextSource)
            {
                _nextPlaybackSource = nextSource;
                _nextInitialVolume = nextItem.Volume ?? 1.0;

                // 初始音量设为 0，等待淡入
                ctx.Player.SetPlaybackSourceOutputVolume(0, _nextPlaybackSource);
            }
            else
            {
                // 加载失败，重置预加载状态
                _preloaded = false;
                _nextItem = null;
            }
        }
        finally
        {
            _loaderSemaphore.Release();
        }
    }

    /// <summary>
    /// 处理下一曲目的淡入效果。
    /// 音量从 0 线性增长到目标音量，增长曲线基于已播放时间 / CrossFadeTime。
    /// </summary>
    private void ProcessFadeIn(TrackTransitionContext ctx)
    {
        try
        {
            if (_nextPlaybackSource is null) return;

            // 确保下一首已开始播放
            ctx.Player.PlayPlaybackSource(_nextPlaybackSource);

            // 基于当前曲目剩余时间计算淡入进度
            var remaining = (ctx.Duration - ctx.Position).TotalSeconds;
            var elapsed = _setting.CrossFadeTime - remaining;
            var multiplier = Math.Clamp(elapsed / _setting.CrossFadeTime, 0, 1);

            var targetVolume = _setting.EnableAudioGain ? _nextInitialVolume : 1.0;
            ctx.Player.SetPlaybackSourceOutputVolume(targetVolume * multiplier, _nextPlaybackSource);

            if (multiplier >= 1.0)
            {
                // 淡入完成
                _processing = false;
                _preloaded = false;
                _nextPlaybackSource = null;
                _nextItem = null;
            }
        }
        catch
        {
            // 忽略播放源可能已被释放的异常
        }
    }

    /// <summary>
    /// 处理当前曲目的淡出效果。
    /// 音量从目标音量线性降低到 0，降低曲线基于 (CrossFadeTime - 剩余时间) / CrossFadeTime。
    /// </summary>
    private void ProcessFadeOut(TrackTransitionContext ctx, double remainingSeconds)
    {
        try
        {
            if (_currentPlaybackSource is null) return;

            var multiplier = Math.Clamp(remainingSeconds / _setting.CrossFadeTime, 0, 1);
            var targetVolume = _setting.EnableAudioGain ? _currentInitialVolume : 1.0;
            ctx.Player.SetPlaybackSourceOutputVolume(targetVolume * multiplier, _currentPlaybackSource);
        }
        catch
        {
            // 忽略播放源可能已被释放的异常
        }
    }

    /// <summary>
    /// 内部重置，清理所有引用和状态标志。
    /// </summary>
    private void ResetInternal()
    {
        _processing = false;
        _preloaded = false;
        _currentPlaybackSource = null;
        _nextPlaybackSource = null;
        _nextItem = null;
        _currentInitialVolume = 1.0;
        _nextInitialVolume = 1.0;
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loaderSemaphore.Dispose();
        ResetInternal();
    }
}
