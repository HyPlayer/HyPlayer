using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
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

namespace HyPlayer.Application.State;

public sealed class UserLibraryStateService(
    IUserLibraryTypeIds userLibraryTypeIds,
    IProviderKnownTypeIds knownTypeIds) : IUserLibraryStateService
{
    private readonly List<ContainerBase> _ownedPlaylists = [];
    private readonly List<ContainerBase> _subscribedPlaylists = [];

    public ContainerBase? LikedSongsPlaylist { get; private set; }
    public IReadOnlyList<ContainerBase> OwnedPlaylists => _ownedPlaylists;
    public IReadOnlyList<ContainerBase> SubscribedPlaylists => _subscribedPlaylists;
    public IReadOnlyList<ContainerBase> UserPlaylists => (List<ContainerBase>)[.. _ownedPlaylists, .. _subscribedPlaylists];

    public void Clear()
    {
        LikedSongsPlaylist = null;
        _ownedPlaylists.Clear();
        _subscribedPlaylists.Clear();
    }

    public void UpdateFromNavigationGroups(IReadOnlyList<ProviderLibraryNavigationGroup> groups)
    {
        Clear();

        var ownedIds = new HashSet<string>();
        var subscribedIds = new HashSet<string>();
        foreach (var group in groups.OrderBy(group => group.DisplayOrder))
        {
            foreach (var item in group.Items.Where(item => item.TypeId == knownTypeIds.PlaylistTypeId))
            {
                if (string.IsNullOrWhiteSpace(item.ActualId))
                    continue;

                if (group.Id == userLibraryTypeIds.LikedSongsTypeId)
                {
                    LikedSongsPlaylist ??= item;
                    AddUnique(_ownedPlaylists, ownedIds, item);
                    continue;
                }

                if (item is IHasLibraryState { IsInCurrentUserLibrary: true, IsOwnedByCurrentUser: false })
                {
                    AddUnique(_subscribedPlaylists, subscribedIds, item);
                    continue;
                }

                if (item is not IHasLibraryState libraryState || libraryState.IsOwnedByCurrentUser)
                    AddUnique(_ownedPlaylists, ownedIds, item);
            }
        }
    }

    public ContainerBase? FindUserPlaylist(string? playlistId)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
            return null;

        return UserPlaylists.FirstOrDefault(playlist => playlist.ActualId == playlistId);
    }

    public bool IsLikedSongsPlaylist(string? playlistId)
    {
        return !string.IsNullOrWhiteSpace(playlistId)
            && LikedSongsPlaylist?.ActualId == playlistId;
    }

    private static void AddUnique(List<ContainerBase> playlists, HashSet<string> ids, ContainerBase playlist)
    {
        if (ids.Add(playlist.ActualId))
            playlists.Add(playlist);
    }
}
