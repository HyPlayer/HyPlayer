using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Services.Playback;

internal static class PlayCoreQueueSnapshot
{
    public static IReadOnlyList<SingleSongBase> GetPlaylist(PlayCoreBase playCore)
    {
        try
        {
            return playCore.GetPlaylistAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<SingleSongBase> GetOrderedPlaylist(PlayCoreBase playCore)
    {
        try
        {
            return playCore.GetOrderedPlaylistAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return GetPlaylist(playCore);
        }
    }

    public static int GetCurrentIndex(PlayCoreBase playCore)
    {
        try
        {
            return playCore.GetCurrentIndexAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return -1;
        }
    }

    public static IReadOnlyList<PlaybackQueueItemSnapshot> GetQueueItems(PlayCoreBase playCore)
        => GetPlaylist(playCore).Select(CreateQueueItemSnapshot).ToArray();

    private static PlaybackQueueItemSnapshot CreateQueueItemSnapshot(SingleSongBase providerItem, int index)
    {
        return new PlaybackQueueItemSnapshot(
            index,
            providerItem.Name ?? string.Empty,
            providerItem is IHasTranslation translatedProvider ? translatedProvider.Translation ?? string.Empty : string.Empty,
            providerItem.CreatorList is { Count: > 0 } creators ? string.Join("; ", creators) : string.Empty,
            providerItem);
    }
}
