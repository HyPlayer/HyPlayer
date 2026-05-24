using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 单曲循环策略：曲目自然结束时重播当前曲目。
/// 手动切歌（GetNext/GetPrevious）仍按顺序移动。
/// </summary>
public sealed class SingleRepeatStrategy : IPlayStrategy
{
    /// <inheritdoc />
    public string Id => "sgl";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        if (ctx.QueueCount == 0) return null;
        var next = ctx.CurrentIndex + 1;
        return next >= ctx.QueueCount ? 0 : next;
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx)
    {
        if (ctx.QueueCount == 0) return null;
        var prev = ctx.CurrentIndex - 1;
        return prev < 0 ? ctx.QueueCount - 1 : prev;
    }

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx) { }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.Replay;
}
