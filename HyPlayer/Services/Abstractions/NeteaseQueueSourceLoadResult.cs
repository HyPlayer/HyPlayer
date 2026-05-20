using HyPlayer.Domain.Music;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 队列源加载结果 — 包含 NCSong 批次列表。
/// 由 <see cref="IQueueSourceProvider"/> 产生，由 <see cref="IPlaylistService"/> 消费。
/// </summary>
public sealed class NeteaseQueueSourceLoadResult
{
    public bool Success { get; init; }

    public IList<IList<NCSong>> Batches { get; init; } = [];

    public static NeteaseQueueSourceLoadResult Failed { get; } = new() { Success = false };

    public static NeteaseQueueSourceLoadResult FromSongs(IList<NCSong> songs) => new()
    {
        Success = true,
        Batches = songs is { Count: > 0 } ? [songs] : []
    };

    public static NeteaseQueueSourceLoadResult FromBatches(IList<IList<NCSong>> batches) => new()
    {
        Success = true,
        Batches = batches?.Where(batch => batch is { Count: > 0 }).ToList() ?? []
    };
}
