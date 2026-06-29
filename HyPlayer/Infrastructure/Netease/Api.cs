#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace HyPlayer.Infrastructure.Netease;

internal class Api
{
    public static async Task<bool> LikeSong(string songid, bool like)
    {
        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        try
        {
            if (like)
            {
                notification.ShowMessage("暂不支持收藏", "当前抽象只支持从集合中移出项目");
            }
            else
            {
                var libraryTypeIds = Ioc.Default.GetRequiredService<IUserLibraryTypeIds>();
                var itemManagement = Ioc.Default.GetRequiredService<IContainerItemManagementProvidable>();
                await itemManagement.RemoveItemFromContainerAsync(libraryTypeIds.LikedSongsTypeId, NormalizeProviderItemId(songid));
            }
            return true;
        }
        catch (System.Exception ex)
        {
            notification.ShowMessage(ex.Message);
            return false;
        }
    }

    public static Task EnterIntelligencePlay(CancellationToken cancellationToken = default)
    {
        return EnterIntelligencePlayCoreAsync(null, null, cancellationToken);
    }

    public static Task EnterIntelligencePlay(string? playlistId, CancellationToken cancellationToken = default)
    {
        return EnterIntelligencePlayCoreAsync(playlistId, null, cancellationToken);
    }

    public static Task EnterIntelligencePlay(
        string? playlistId,
        string? seedSongId,
        CancellationToken cancellationToken = default)
    {
        return EnterIntelligencePlayCoreAsync(playlistId, seedSongId, cancellationToken);
    }

    private static async Task EnterIntelligencePlayCoreAsync(
        string? playlistId,
        string? seedSongId,
        CancellationToken cancellationToken = default)
    {
        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        var itemProvider = Ioc.Default.GetRequiredService<IProvidableItemProvidable>();
        var specialTypeIds = Ioc.Default.GetRequiredService<IProviderSpecialContainerTypeIds>();
        var playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
        var control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
        var state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        var auth = Ioc.Default.GetRequiredService<IAuthService>();
        var userLibrary = Ioc.Default.GetRequiredService<IUserLibraryStateService>();

        var seedPlaylist = await ResolveLikedMusicPlaylistAsync(auth, userLibrary, playlistId, cancellationToken);
        if (seedPlaylist is null || string.IsNullOrWhiteSpace(seedPlaylist.ActualId))
        {
            notification.ShowMessage("无法进入心动模式", "未找到我喜欢的音乐歌单");
            return;
        }

        var seedSongIds = auth.LikedSongs
            .Select(NormalizeProviderItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
        if (seedSongIds.Count == 0)
            seedSongIds = await GetPlaylistSongIdsAsync(seedPlaylist, cancellationToken);

        if (seedSongIds.Count == 0)
        {
            notification.ShowMessage("无法进入心动模式", "心动模式歌单没有歌曲");
            return;
        }

        seedSongId = NormalizeProviderItemId(seedSongId);
        var currentSongId = NormalizeProviderItemId(state.NowPlayingProviderItem?.ActualId);
        var randomSongId = seedSongIds[RandomNumberGenerator.GetInt32(seedSongIds.Count)];
        var seedSong = !string.IsNullOrWhiteSpace(seedSongId) ? seedSongId
            : !string.IsNullOrWhiteSpace(currentSongId) && seedSongIds.Contains(currentSongId) ? currentSongId
            : randomSongId;
        try
        {
            var songs = await GetIntelligenceSongsAsync(itemProvider, specialTypeIds, seedSong, cancellationToken);
            if (songs.Count == 0)
            {
                notification.ShowMessage("无法进入心动模式", "没有获取到推荐歌曲");
                return;
            }

            if (state.IsInFm)
                PersonalFM.ExitFm(clearPlaylist: false);

            await playCore.StopAsync(cancellationToken);
            await playCore.RemoveAllSongAsync(cancellationToken);
            state.IsInFm = false;

            await playCore.InsertSongRangeAsync(songs, ctk: cancellationToken);
            await playCore.MovePointerToAsync(songs[0], cancellationToken);
            await control.LoadAndPlayAsync(songs[0], removeCurrentSongs: false);
        }
        catch (System.Exception ex)
        {
            notification.ShowMessage("加载心动模式列表出错", ex.Message);
        }
    }

    private static async Task<List<SingleSongBase>> GetIntelligenceSongsAsync(
        IProvidableItemProvidable itemProvider,
        IProviderSpecialContainerTypeIds specialTypeIds,
        string seedSong,
        CancellationToken cancellationToken)
    {
        if (!specialTypeIds.SpecialContainerTypeIds.TryGetValue(SpecialContainerType.ContextRecommendation, out var typeId))
            return [];

        return await itemProvider.GetProvidableItemByIdAsync(typeId + seedSong, cancellationToken) is LinerContainerBase container
            ? (await container.GetAllItemsAsync(cancellationToken)).OfType<SingleSongBase>().ToList()
            : [];
    }

    private static async Task<List<string>> GetPlaylistSongIdsAsync(
        ContainerBase playlist,
        CancellationToken cancellationToken)
    {
        if (playlist is not IProgressiveLoadingContainer progressive)
            return [];

        var (_, items) = await progressive.GetProgressiveItemsListAsync(0, progressive.MaxProgressiveCount, cancellationToken);
        return items.OfType<SingleSongBase>()
            .Select(song => NormalizeProviderItemId(song.ActualId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    private static async Task<ContainerBase?> ResolveLikedMusicPlaylistAsync(
        IAuthService auth,
        IUserLibraryStateService userLibrary,
        string? playlistId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(playlistId))
        {
            return userLibrary.FindUserPlaylist(playlistId);
        }

        var cached = userLibrary.LikedSongsPlaylist;
        if (cached is not null)
            return cached;

        if (auth.CurrentUser is null)
            return null;

        var containers = await auth.CurrentUser.GetSubContainerAsync(cancellationToken);
        return FindLikedMusicPlaylist(containers);
    }

    private static ContainerBase? FindLikedMusicPlaylist(IEnumerable<ContainerBase> containers)
    {
        return containers.FirstOrDefault(container => container is not HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem.IHasLibraryState state || state.IsOwnedByCurrentUser);
    }

    private static string? NormalizeProviderItemId(string? songId)
    {
        return songId;
    }
}
