using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 队列源加载结果 — 包含提供者歌曲批次列表。
/// 由 <see cref="IQueueSourceProvider"/> 产生，由 PlayCore 队列加载服务消费。
/// </summary>
public sealed class ProviderQueueSourceLoadResult
{
    public bool Success { get; init; }

    public IList<IList<SingleSongBase>> Batches { get; init; } = [];

    public static ProviderQueueSourceLoadResult Failed { get; } = new() { Success = false };

    public static ProviderQueueSourceLoadResult FromSongs(IList<SingleSongBase> songs) => new()
    {
        Success = true,
        Batches = songs is { Count: > 0 } ? [songs] : []
    };

    public static ProviderQueueSourceLoadResult FromBatches(IList<IList<SingleSongBase>> batches) => new()
    {
        Success = true,
        Batches = batches?.Where(batch => batch is { Count: > 0 }).ToList() ?? []
    };
}
