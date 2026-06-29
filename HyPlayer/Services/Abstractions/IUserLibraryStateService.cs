using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using System.Collections.Generic;

namespace HyPlayer.Services.Abstractions;

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
