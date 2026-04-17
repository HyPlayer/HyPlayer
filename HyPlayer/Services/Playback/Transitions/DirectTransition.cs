using System.Threading.Tasks;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Transitions;

/// <summary>
/// 直接切歌过渡策略，无任何过渡效果。
/// <para>
/// 当曲目自然结束时，立即请求下一首并加载播放。
/// 手动切歌和位置更新均为空操作。
/// </para>
/// </summary>
public sealed class DirectTransition : ITrackTransition
{
    /// <inheritdoc />
    public string Id => "dir";

    /// <summary>
    /// 位置更新回调 — 直接切歌无需预加载，空操作。
    /// </summary>
    public void OnPositionTick(TrackTransitionContext ctx)
    {
        // 直接切歌不需要在播放过程中做任何处理
    }

    /// <summary>
    /// 曲目自然结束时，请求下一首并加载。
    /// </summary>
    public async Task OnTrackEndedAsync(TrackTransitionContext ctx)
    {
        var nextItem = await ctx.RequestNextItemAsync(true).ConfigureAwait(false);
        if (nextItem is not null)
        {
            await ctx.LoadMediaSourceAsync(nextItem, true, true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 手动切歌 — 直接切歌无需中断任何过渡，空操作。
    /// </summary>
    public Task OnManualSkipAsync(TrackTransitionContext ctx)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重置状态 — 直接切歌无状态，空操作。
    /// </summary>
    public void Reset()
    {
        // 无状态需要清理
    }
}
