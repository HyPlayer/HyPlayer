using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.Services;

internal static class PlayCoreQueueSnapshot
{
    public static async Task<IReadOnlyList<SingleSongBase>> GetPlaylistAsync(PlayCoreBase playCore)
    {
        try { return await playCore.GetPlaylistAsync().ConfigureAwait(false); }
        catch { return []; }
    }

    public static async Task<IReadOnlyList<SingleSongBase>> GetOrderedPlaylistAsync(PlayCoreBase playCore)
    {
        try { return await playCore.GetOrderedPlaylistAsync().ConfigureAwait(false); }
        catch { return await GetPlaylistAsync(playCore).ConfigureAwait(false); }
    }

    internal static PlaybackQueueItemSnapshot[] Build(
        IReadOnlyList<SingleSongBase> queue, IReadOnlyList<SingleSongBase>? orderedQueue = null)
    {
        var snapshots = queue.Select(CreateQueueItemSnapshot).ToArray();
        if (orderedQueue is null) return snapshots;
        var indices = new Dictionary<(string Provider, string Type, string? Id), int>();
        for (var i = 0; i < queue.Count; i++)
        {
            var song = queue[i];
            indices.TryAdd((song.ProviderId, song.TypeId, song.ActualId), i);
        }
        var ordered = new List<PlaybackQueueItemSnapshot>(orderedQueue.Count);
        foreach (var song in orderedQueue)
            if (indices.TryGetValue((song.ProviderId, song.TypeId, song.ActualId), out var index))
                ordered.Add(snapshots[index]);
        return ordered.ToArray();
    }

    private static PlaybackQueueItemSnapshot CreateQueueItemSnapshot(SingleSongBase providerItem, int index) =>
        new(index, providerItem.Name ?? string.Empty,
            providerItem is IHasTranslation translatedProvider ? translatedProvider.Translation ?? string.Empty : string.Empty,
            providerItem.CreatorList is { Count: > 0 } creators ? string.Join("; ", creators) : string.Empty,
            providerItem);
}
