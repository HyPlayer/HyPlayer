using System.Collections.Generic;
using HyPlayer.PlayCore.Abstraction.Models;

namespace HyPlayer.Application.State;

public interface IUserLibraryStateService
{
    ContainerBase? LikedSongsPlaylist { get; }
    IReadOnlyList<ContainerBase> OwnedPlaylists { get; }
    IReadOnlyList<ContainerBase> SubscribedPlaylists { get; }
    IReadOnlyList<ContainerBase> UserPlaylists { get; }

    void Clear();
    void UpdateFromNavigationGroups(IReadOnlyList<ProviderLibraryNavigationGroup> groups);
    ContainerBase? FindUserPlaylist(string? playlistId);
    bool IsLikedSongsPlaylist(string? playlistId);
}