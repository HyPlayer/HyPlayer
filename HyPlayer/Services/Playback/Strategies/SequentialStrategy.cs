using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 列表循环策略：按顺序播放，到末尾后回到开头。
/// </summary>
public sealed class SequentialStrategy : IPlayStrategy
{
    /// <inheritdoc />
    public string Id => "seq";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        var next = ctx.CurrentIndex + 1;
        return next >= ctx.Items.Count ? 0 : next;
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        var prev = ctx.CurrentIndex - 1;
        return prev < 0 ? ctx.Items.Count - 1 : prev;
    }

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx) { }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.MoveNext;
}
