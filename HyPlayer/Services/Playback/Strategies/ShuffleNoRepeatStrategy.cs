using System;
using System.Collections.Generic;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 不重复随机播放策略：使用 Fisher-Yates 预生成乱序表，
/// 保证每首歌播放一次后才重新洗牌。
/// </summary>
public sealed class ShuffleNoRepeatStrategy : IPlayStrategy
{
    private readonly Random _random = new();
    private readonly List<int> _shuffleTable = [];
    private int _shufflingIndex;

    /// <inheritdoc />
    public string Id => "shn";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        EnsureTable(ctx.Items.Count);
        _shufflingIndex++;
        if (_shufflingIndex >= _shuffleTable.Count)
            _shufflingIndex = 0;
        return _shuffleTable[_shufflingIndex];
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        EnsureTable(ctx.Items.Count);
        _shufflingIndex--;
        if (_shufflingIndex < 0)
            _shufflingIndex = _shuffleTable.Count - 1;
        return _shuffleTable[_shufflingIndex];
    }

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx)
    {
        RebuildTable(ctx.Items.Count);
        // 将当前播放索引定位到乱序表中对应位置
        var pos = _shuffleTable.IndexOf(ctx.CurrentIndex);
        _shufflingIndex = pos >= 0 ? pos : 0;
    }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.MoveNext;

    /// <summary>
    /// 确保乱序表已初始化且大小匹配
    /// </summary>
    private void EnsureTable(int count)
    {
        if (_shuffleTable.Count != count)
            RebuildTable(count);
    }

    /// <summary>
    /// 使用 Fisher-Yates 算法重建乱序表
    /// </summary>
    private void RebuildTable(int count)
    {
        _shuffleTable.Clear();
        for (var i = 0; i < count; i++)
            _shuffleTable.Add(i);

        // Fisher-Yates shuffle
        for (var i = count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_shuffleTable[i], _shuffleTable[j]) = (_shuffleTable[j], _shuffleTable[i]);
        }

        _shufflingIndex = 0;
    }
}
