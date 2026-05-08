using System;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 曲目过渡上下文
/// </summary>
public class TrackTransitionContext
{
    /// <summary>当前播放位置</summary>
    public TimeSpan Position { get; init; }

    /// <summary>当前曲目总时长</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>当前曲目</summary>
    public HyPlayItem? CurrentItem { get; init; }

    /// <summary>请求获取下一首曲目（由 PlaylistService 提供的回调）</summary>
    public required Func<bool, Task<HyPlayItem?>> RequestNextItemAsync { get; init; }

    /// <summary>提交预加载曲目为当前曲目（由 PlaylistService 提供的回调）</summary>
    public required Func<HyPlayItem, Task> CommitItemAsync { get; init; }

    /// <summary>请求加载指定曲目的媒体源</summary>
    public required Func<HyPlayItem, bool, bool, bool, Task> LoadMediaSourceAsync { get; init; }

    /// <summary>底层播放器</summary>
    public required IPlayer Player { get; init; }
}

/// <summary>
/// 曲目过渡策略，决定"怎么从当前曲目过渡到下一曲目"
/// <para>
/// 每种过渡用三字母 Id 标识：
/// <list type="bullet">
///   <item><c>dir</c> — 直接切歌，无过渡效果</item>
///   <item><c>xfd</c> — 交叉淡入淡出（Cross-Fade）</item>
///   <item><c>gap</c> — 无缝衔接（Gapless，预留）</item>
/// </list>
/// </para>
/// </summary>
public interface ITrackTransition
{
    /// <summary>三字母过渡标识</summary>
    string Id { get; }

    /// <summary>
    /// 每次播放位置更新时调用，用于预加载和渐变处理
    /// </summary>
    void OnPositionTick(TrackTransitionContext ctx);

    /// <summary>
    /// 当前曲目自然播放结束时调用
    /// </summary>
    Task OnTrackEndedAsync(TrackTransitionContext ctx);

    /// <summary>
    /// 用户手动切歌时调用（需要中断正在进行的过渡）
    /// </summary>
    Task OnManualSkipAsync(TrackTransitionContext ctx);

    /// <summary>
    /// 重置状态（清理预加载资源等）
    /// </summary>
    void Reset();
}
