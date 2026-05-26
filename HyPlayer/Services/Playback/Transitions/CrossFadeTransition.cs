using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.Transitions;

/// <summary>
/// 交叉淡入淡出过渡策略。
/// <para>
/// 在当前曲目即将结束时预加载下一首，并在剩余时间 ≤ <see cref="HyPlayer.App.Setting.CrossFadeTime"/> 时
/// 同时对当前曲目执行淡出、对下一曲目执行淡入，实现平滑过渡。
/// </para>
/// </summary>
public sealed partial class CrossFadeTransition : ITrackTransition, IDisposable
{
    private readonly Setting _setting;

    private IPlaybackSource? _currentPlaybackSource;
    private ITransitionPlaybackSource? _nextPlaybackSource;

    private double _currentInitialVolume = 1.0;
    private double _nextInitialVolume = 1.0;

    private readonly SemaphoreSlim _loaderSemaphore = new(1, 1);

    /// <summary>是否已预加载下一首</summary>
    private volatile bool _preloaded;

    /// <summary>是否正在预加载下一首</summary>
    private volatile bool _preloading;

    /// <summary>是否正在执行淡入淡出</summary>
    private volatile bool _processing;

    /// <summary>下一首是否已提交为当前播放项</summary>
    private volatile bool _committedNext;

    /// <summary>本次淡入完成后已提交的播放源</summary>
    private ITransitionPlaybackSource? _committedPlaybackSource;

    /// <summary>淡入淡出开始时的单调时钟刻度</summary>
    private long _fadeStartTicks;

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
    public async void OnPositionTick(TrackTransitionContext ctx)
    {
        if (!_setting.CrossFade) return;

        // 尝试预加载
        ctx.TaskRunner.Forget(TryPreloadAsync(ctx), "cross-fade preload next track");

        if (!_preloaded) return;

        var remaining = (ctx.Duration - ctx.Position).TotalSeconds;

        // 开始淡入淡出
        if (remaining <= _setting.CrossFadeTime && !_processing)
        {
            _processing = true;
            _fadeStartTicks = Stopwatch.GetTimestamp();

            if (_nextPlaybackSource is not null)
            {
                await _nextPlaybackSource.PlayAsync().ConfigureAwait(false);
                await _nextPlaybackSource.SetAsPrimaryAsync().ConfigureAwait(false);
            }

            if (!_committedNext && _nextPlaybackSource is not null && ctx.CommitProviderItemAsync is not null)
            {
                _committedPlaybackSource = _nextPlaybackSource;
                ctx.TaskRunner.Forget(ctx.CommitProviderItemAsync(_nextPlaybackSource.Item), "commit cross-fade preloaded provider item");
                _committedNext = true;
            }
        }

        if (!_processing) return;

        var multiplier = GetFadeMultiplier();

        // 淡出当前曲目
        if (_currentPlaybackSource is not null)
        {
            ProcessFadeOut(ctx, multiplier);
        }

        // 淡入下一曲目
        if (_nextPlaybackSource is not null)
        {
            await ProcessFadeInAsync(ctx, multiplier).ConfigureAwait(false);
        }

        if (multiplier >= 1.0)
        {
            await CompleteFadeAsync(ctx).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 当前曲目自然结束时调用。
    /// 如果已预加载，则断开旧源并清理；否则回退到直接切歌。
    /// </summary>
    public async Task OnTrackEndedAsync(TrackTransitionContext ctx)
    {
        var nextPlaybackSource = _nextPlaybackSource ?? _committedPlaybackSource;

        if (_currentPlaybackSource is not null && nextPlaybackSource is not null)
        {
            var targetVolume = _setting.EnableAudioGain ? _nextInitialVolume : 1.0;
            await nextPlaybackSource.PlayAsync().ConfigureAwait(false);
            await nextPlaybackSource.SetVolumeAsync(targetVolume).ConfigureAwait(false);
            await nextPlaybackSource.SetAsPrimaryAsync().ConfigureAwait(false);

            if (!_committedNext && ctx.CommitProviderItemAsync is not null)
            {
                await ctx.CommitProviderItemAsync(nextPlaybackSource.Item).ConfigureAwait(false);
                _committedNext = true;
            }

            // 已经在淡入淡出中或已完成预加载 — 断开旧源
            ctx.Player.DisconnectPlaybackSource(_currentPlaybackSource);

            _currentPlaybackSource = null;
        }
        else
        {
            // 未预加载 — 回退到直接切歌
            ResetInternal();
            if (ctx.RequestNextProviderItemAsync is null || ctx.LoadProviderMediaSourceAsync is null)
                return;

            var fallbackItem = await ctx.RequestNextProviderItemAsync(true).ConfigureAwait(false);
            if (fallbackItem is not null)
                await ctx.LoadProviderMediaSourceAsync(fallbackItem, true, true).ConfigureAwait(false);
            return;
        }

        // 清理淡入淡出状态
        _processing = false;
        _preloaded = false;
        _preloading = false;
        _nextPlaybackSource = null;
        _fadeStartTicks = 0;
    }

    /// <summary>
    /// 用户手动切歌时调用 — 取消正在进行的淡入淡出，断开预加载源，重置状态。
    /// </summary>
    public async Task OnManualSkipAsync(TrackTransitionContext ctx)
    {
        if (!_processing && !_preloaded) return;

        // 断开并清理当前源
        if (_currentPlaybackSource is not null)
        {
            ctx.Player.DisconnectPlaybackSource(_currentPlaybackSource);
        }

        // 断开并清理预加载的下一首源
        if (_nextPlaybackSource is not null)
        {
            await _nextPlaybackSource.DisposeAsync();
        }

        ResetInternal();
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
        if (_preloaded || _preloading || _processing || !_setting.CrossFade) return;
        if (ctx.RequestNextProviderItemAsync is null || ctx.PreloadProviderPlaybackSourceAsync is null) return;

        try
        {
            _preloading = true;
            await _loaderSemaphore.WaitAsync().ConfigureAwait(false);
            if (_preloaded || _processing) return;

            var totalSeconds = ctx.Duration.TotalSeconds;
            var currentSeconds = ctx.Position.TotalSeconds;
            var remaining = totalSeconds - currentSeconds;
            var targetTime = Math.Max(totalSeconds * 0.125, 20);

            if (remaining > targetTime || totalSeconds <= 10) return;

            _preloaded = true;

            // 记录当前播放源
            if (ctx.Player.PrimaryPlaybackSource is { } currentSource)
            {
                _currentPlaybackSource = currentSource;
                _currentInitialVolume = 1.0;
            }

            // 请求下一首
            var nextItem = await ctx.RequestNextProviderItemAsync(false).ConfigureAwait(false);
            if (nextItem is null)
            {
                _preloaded = false;
                return;
            }

            _committedNext = false;
            _committedPlaybackSource = null;

            _nextPlaybackSource = await ctx.PreloadProviderPlaybackSourceAsync(nextItem).ConfigureAwait(false);
            if (_nextPlaybackSource is not null)
            {
                _nextInitialVolume = _nextPlaybackSource.SuggestedVolume;

                // 初始音量设为 0，等待淡入
                await _nextPlaybackSource.SetVolumeAsync(0).ConfigureAwait(false);
            }
            else
            {
                // 加载失败，重置预加载状态
                _preloaded = false;
            }
        }
        finally
        {
            _preloading = false;
            _loaderSemaphore.Release();
        }
    }

    /// <summary>
    /// 处理下一曲目的淡入效果。
    /// 音量从 0 线性增长到目标音量，增长曲线基于已播放时间 / CrossFadeTime。
    /// </summary>
    private async Task ProcessFadeInAsync(TrackTransitionContext ctx, double multiplier)
    {
        try
        {
            if (_nextPlaybackSource is null) return;

            // 确保下一首已开始播放
            await _nextPlaybackSource.PlayAsync().ConfigureAwait(false);

            var targetVolume = _setting.EnableAudioGain ? _nextInitialVolume : 1.0;
            await _nextPlaybackSource.SetVolumeAsync(targetVolume * multiplier).ConfigureAwait(false);
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
    private void ProcessFadeOut(TrackTransitionContext ctx, double multiplier)
    {
        try
        {
            if (_currentPlaybackSource is null) return;

            var targetVolume = _setting.EnableAudioGain ? _currentInitialVolume : 1.0;
            ctx.Player.SetPlaybackSourceOutputVolume(targetVolume * (1 - multiplier), _currentPlaybackSource);
        }
        catch
        {
            // 忽略播放源可能已被释放的异常
        }
    }

    private double GetFadeMultiplier()
    {
        if (_fadeStartTicks == 0) return 0;

        var crossFadeSeconds = Math.Max(_setting.CrossFadeTime, 0.001);
        var elapsedSeconds = (Stopwatch.GetTimestamp() - _fadeStartTicks) / (double)Stopwatch.Frequency;
        return Math.Clamp(elapsedSeconds / crossFadeSeconds, 0, 1);
    }

    private async Task CompleteFadeAsync(TrackTransitionContext ctx)
    {
        if (_nextPlaybackSource is not null)
        {
            var targetVolume = _setting.EnableAudioGain ? _nextInitialVolume : 1.0;
            await _nextPlaybackSource.SetVolumeAsync(targetVolume).ConfigureAwait(false);
            await _nextPlaybackSource.SetAsPrimaryAsync().ConfigureAwait(false);
        }

        if (_currentPlaybackSource is not null)
        {
            ctx.Player.DisconnectPlaybackSource(_currentPlaybackSource);
        }

        _currentPlaybackSource = null;
        _nextPlaybackSource = null;
        _processing = false;
        _preloaded = false;
        _preloading = false;
        _fadeStartTicks = 0;
    }

    /// <summary>
    /// 内部重置，清理所有引用和状态标志。
    /// </summary>
    private void ResetInternal()
    {
        if (!_committedNext && _nextPlaybackSource is not null)
            _ = _nextPlaybackSource.DisposeAsync();

        _processing = false;
        _preloaded = false;
        _preloading = false;
        _currentPlaybackSource = null;
        _nextPlaybackSource = null;
        _currentInitialVolume = 1.0;
        _nextInitialVolume = 1.0;
        _committedNext = false;
        _committedPlaybackSource = null;
        _fadeStartTicks = 0;
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
