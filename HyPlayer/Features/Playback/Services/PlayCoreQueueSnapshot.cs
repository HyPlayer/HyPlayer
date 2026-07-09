using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Features.Playback.Services;

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
