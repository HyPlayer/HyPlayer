using HyPlayer.Services.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.Transitions;

/// <summary>
/// 无缝衔接过渡策略（Gapless）。
/// <para>
/// 在当前曲目即将结束时预加载下一首，曲目结束后无缝切换，
/// 不执行任何音量渐变。此为预留实现，供后续完善。
/// </para>
/// </summary>
public sealed partial class GaplessTransition : ITrackTransition, IDisposable
{
    private readonly SemaphoreSlim _loaderSemaphore = new(1, 1);

    private ITransitionPlaybackSource? _nextPlaybackSource;
    private volatile bool _preloaded;
    private volatile bool _preloading;
    private bool _disposed;

    /// <inheritdoc />
    public string Id => "gap";

    /// <summary>
    /// 每次播放位置更新时调用。
    /// 当剩余时间 ≤ max(总时长×0.125, 20s) 且总时长 &gt; 10s 时预加载下一首。
    /// </summary>
    public void OnPositionTick(TrackTransitionContext ctx)
    {
        ctx.TaskRunner.Forget(TryPreloadAsync(ctx), "gapless preload next track");
    }

    /// <summary>
    /// 当前曲目自然结束时调用。
    /// 如果已预加载，则开始播放下一首；否则回退到直接切歌。
    /// </summary>
    public async Task OnTrackEndedAsync(TrackTransitionContext ctx)
    {
        if (_preloaded && _nextPlaybackSource is not null)
        {
            var oldSource = ctx.Player.PrimaryPlaybackSource;

            if (ctx.CommitProviderItemAsync is not null)
                await ctx.CommitProviderItemAsync(_nextPlaybackSource.Item).ConfigureAwait(false);

            await _nextPlaybackSource.SetAsPrimaryAsync().ConfigureAwait(false);
            await _nextPlaybackSource.PlayAsync().ConfigureAwait(false);

            if (oldSource is not null && !ReferenceEquals(oldSource, ctx.Player.PrimaryPlaybackSource))
            {
                ctx.Player.DisconnectPlaybackSource(oldSource);
            }

            ResetInternal(disposeNext: false);
        }
        else
        {
            // 未预加载 — 回退到直接切歌
            ResetInternal();
            if (ctx.RequestNextProviderItemAsync is null || ctx.LoadProviderMediaSourceAsync is null)
                return;

            var nextProviderItem = await ctx.RequestNextProviderItemAsync(true).ConfigureAwait(false);
            if (nextProviderItem is not null)
                await ctx.LoadProviderMediaSourceAsync(nextProviderItem, true, true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 用户手动切歌时调用 — 丢弃预加载的资源，重置状态。
    /// </summary>
    public Task OnManualSkipAsync(TrackTransitionContext ctx)
    {
        if (!_preloaded) return Task.CompletedTask;

        if (_nextPlaybackSource is not null)
        {
            return ResetInternalAsync();
        }

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
    /// </summary>
    private async Task TryPreloadAsync(TrackTransitionContext ctx)
    {
        if (_preloaded || _preloading) return;
        if (ctx.RequestNextProviderItemAsync is null || ctx.PreloadProviderPlaybackSourceAsync is null) return;

        try
        {
            _preloading = true;
            await _loaderSemaphore.WaitAsync().ConfigureAwait(false);
            if (_preloaded) return;

            var totalSeconds = ctx.Duration.TotalSeconds;
            var currentSeconds = ctx.Position.TotalSeconds;
            var remaining = totalSeconds - currentSeconds;
            var targetTime = Math.Max(totalSeconds * 0.125, 20);

            if (remaining > targetTime || totalSeconds <= 10) return;

            _preloaded = true;

            var nextItem = await ctx.RequestNextProviderItemAsync(false).ConfigureAwait(false);
            if (nextItem is null)
            {
                _preloaded = false;
                return;
            }

            _nextPlaybackSource = await ctx.PreloadProviderPlaybackSourceAsync(nextItem).ConfigureAwait(false);
            if (_nextPlaybackSource is null)
            {
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
    /// 内部重置，清理所有引用和状态标志。
    /// </summary>
    private void ResetInternal(bool disposeNext = true)
    {
        if (disposeNext && _nextPlaybackSource is not null)
            _ = _nextPlaybackSource.DisposeAsync();

        _preloaded = false;
        _preloading = false;
        _nextPlaybackSource = null;
    }

    private async Task ResetInternalAsync()
    {
        if (_nextPlaybackSource is not null)
            await _nextPlaybackSource.DisposeAsync();

        _preloaded = false;
        _preloading = false;
        _nextPlaybackSource = null;
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
