using System;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 纯随机播放策略：每次随机选取一首，可能重复。
/// </summary>
public sealed class ShuffleStrategy : IPlayStrategy
{
    private readonly Random _random = new();

    /// <inheritdoc />
    public string Id => "shf";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        return _random.Next(ctx.Items.Count);
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        return _random.Next(ctx.Items.Count);
    }

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx) { }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.MoveNext;
}
