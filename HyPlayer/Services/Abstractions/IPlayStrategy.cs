using HyPlayer.Domain.Music;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 播放策略上下文，提供只读视图供策略决策
/// </summary>
public class PlayStrategyContext
{
    /// <summary>当前播放列表</summary>
    public required IReadOnlyList<HyPlayItem> Items { get; init; }

    /// <summary>当前播放索引</summary>
    public required int CurrentIndex { get; init; }

    /// <summary>当前播放曲目</summary>
    public HyPlayItem? CurrentItem { get; init; }
    /// <summary>当前播放列表</summary>
    public IReadOnlyList<int>? ShuffledItems { get; init; }

    /// <summary>随机播放索引</summary>
    public int? ShuffledIndex { get; init; }

    /// <summary>更新随机算法</summary>
    public Action? UpdateShuffleActions { get; init; }
}

/// <summary>
/// 策略返回的行为指令
/// </summary>
public enum PlayStrategyAction
{
    /// <summary>移动到下一首</summary>
    MoveNext,

    /// <summary>重播当前曲目（单曲循环）</summary>
    Replay,

    /// <summary>停止播放</summary>
    Stop,

    /// <summary>需要异步加载更多内容后再决定（FM 模式）</summary>
    LoadMore
}

/// <summary>
/// 播放策略，决定"下一首/上一首是哪首"
/// <para>
/// 每种策略用三字母 Id 标识：
/// <list type="bullet">
///   <item><c>seq</c> — 列表循环</item>
///   <item><c>sgl</c> — 单曲循环</item>
///   <item><c>shf</c> — 随机播放（纯随机）</item>
///   <item><c>shn</c> — 随机播放（不重复）</item>
///   <item><c>pfm</c> — 私人 FM（自动加载后续）</item>
///   <item><c>ltg</c> — 一起听（服务器同步）</item>
/// </list>
/// </para>
/// </summary>
public interface IPlayStrategy
{
    /// <summary>三字母策略标识</summary>
    string Id { get; }

    /// <summary>
    /// 获取下一首的索引。返回 null 表示播放结束（无下一首）
    /// </summary>
    int? GetNext(PlayStrategyContext ctx);

    /// <summary>
    /// 获取上一首的索引。返回 null 表示无上一首
    /// </summary>
    int? GetPrevious(PlayStrategyContext ctx);

    /// <summary>
    /// 当播放列表变化时调用（重建 shuffle 表等）
    /// </summary>
    void OnPlaylistChanged(PlayStrategyContext ctx);

    /// <summary>
    /// 当曲目自然播放结束时调用。
    /// 返回 <see cref="PlayStrategyAction"/> 指示下一步行为
    /// </summary>
    PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx);
}

/// <summary>
/// 支持异步加载更多内容的播放策略（如私人 FM）。
/// 当 <see cref="IPlayStrategy.OnTrackEnded"/> 返回 <see cref="PlayStrategyAction.LoadMore"/> 时，
/// PlaylistService 会调用 <see cref="LoadMoreAsync"/> 获取后续曲目。
/// </summary>
public interface IAsyncPlayStrategy : IPlayStrategy
{
    /// <summary>
    /// 异步加载更多曲目追加到播放列表
    /// </summary>
    Task<IEnumerable<HyPlayItem>> LoadMoreAsync(PlayStrategyContext ctx, CancellationToken ct = default);
}
