using System.Collections.Generic;
using System.Security.Cryptography;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 不重复随机播放策略：使用
/// </summary>
public sealed class ShuffleNoRepeatStrategy : IPlayStrategy
{

    /// <inheritdoc />
    public string Id => "shn";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        if ((ctx.ShuffledItems?.Count ?? 0)== 0) return null;
        int index = ctx.ShuffledIndex.Value + 1;
        if (index >= ctx.ShuffledItems.Count)
        {
            ctx.UpdateShuffleActions?.Invoke();
            index = 1;
        }

        return ctx.ShuffledItems[index];
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx)
    {
        if ((ctx.ShuffledItems?.Count ?? 0) == 0) return null;
        var index = ctx.ShuffledIndex.Value - 1;
        if (index < 0)
        {
            ctx.UpdateShuffleActions?.Invoke();
            index = ctx.ShuffledItems.Count - 1;
        }

        return ctx.ShuffledItems[index];
    }

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx){ }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.MoveNext;
}
