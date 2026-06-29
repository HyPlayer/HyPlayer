using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Services.AppState;

public sealed class UserLibraryStateService : IUserLibraryStateService
{
    private readonly IUserLibraryTypeIds _userLibraryTypeIds;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly List<ContainerBase> _ownedPlaylists = [];
    private readonly List<ContainerBase> _subscribedPlaylists = [];

    public UserLibraryStateService(
        IUserLibraryTypeIds userLibraryTypeIds,
        IProviderKnownTypeIds knownTypeIds)
    {
        _userLibraryTypeIds = userLibraryTypeIds;
        _knownTypeIds = knownTypeIds;
    }

    public ContainerBase? LikedSongsPlaylist { get; private set; }
    public IReadOnlyList<ContainerBase> OwnedPlaylists => _ownedPlaylists;
    public IReadOnlyList<ContainerBase> SubscribedPlaylists => _subscribedPlaylists;
    public IReadOnlyList<ContainerBase> UserPlaylists => [.. _ownedPlaylists, .. _subscribedPlaylists];

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
            foreach (var item in group.Items.Where(item => item.TypeId == _knownTypeIds.PlaylistTypeId))
            {
                if (string.IsNullOrWhiteSpace(item.ActualId))
                    continue;

                if (group.Id == _userLibraryTypeIds.LikedSongsTypeId)
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
