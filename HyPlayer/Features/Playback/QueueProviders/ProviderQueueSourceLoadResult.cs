using System.Collections.Generic;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
///     队列源加载结果 — 包含提供者歌曲批次列表。
///     由 <see cref="IQueueSourceProvider" /> 产生，由 PlayCore 队列加载服务消费。
/// </summary>
public sealed class ProviderQueueSourceLoadResult
{
    public bool Success { get; init; }

    public IList<IList<SingleSongBase>> Batches { get; init; } = [];

    public static ProviderQueueSourceLoadResult Failed { get; } = new() { Success = false };

    public static ProviderQueueSourceLoadResult FromSongs(IList<SingleSongBase> songs)
    {
        return new ProviderQueueSourceLoadResult
        {
            Success = true,
            Batches = songs is { Count: > 0 } ? [songs] : []
        };
    }

    public static ProviderQueueSourceLoadResult FromBatches(IList<IList<SingleSongBase>> batches)
    {
        if (batches is null || batches.Count == 0)
            return new ProviderQueueSourceLoadResult { Success = true, Batches = [] };

        List<IList<SingleSongBase>>? filtered = null;
        for (var i = 0; i < batches.Count; i++)
        {
            if (batches[i] is { Count: > 0 })
            {
                filtered?.Add(batches[i]);
                continue;
            }

            filtered ??= CopyNonEmptyBefore(batches, i);
        }

        return new ProviderQueueSourceLoadResult
        {
            Success = true,
            Batches = filtered ?? batches
        };
    }

    private static List<IList<SingleSongBase>> CopyNonEmptyBefore(IList<IList<SingleSongBase>> batches, int count)
    {
        var result = new List<IList<SingleSongBase>>(count);
        for (var i = 0; i < count; i++)
            if (batches[i] is { Count: > 0 })
                result.Add(batches[i]);

        return result;
    }
}