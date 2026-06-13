#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseProvider.Mappers;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction;
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
            var song = new NeteaseSong
            {
                ActualId = songid.StartsWith(NeteaseTypeIds.SingleSong) ? songid[2..] : songid,
                Name = string.Empty,
                Artists = []
            };

            if (like)
                await song.LikeAsync();
            else
                await song.UnlikeAsync();
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
        var neteaseProvider = Ioc.Default.GetRequiredService<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
        var playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
        var control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
        var state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        var auth = Ioc.Default.GetRequiredService<IAuthService>();

        var seedPlaylist = await ResolveLikedMusicPlaylistAsync(neteaseProvider, auth, playlistId, cancellationToken);
        if (seedPlaylist is null || string.IsNullOrWhiteSpace(seedPlaylist.ActualId))
        {
            notification.ShowMessage("无法进入心动模式", "未找到我喜欢的音乐歌单");
            return;
        }

        var seedSongIds = auth.LikedSongs
            .Select(NormalizeNeteaseSongId)
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

        seedSongId = NormalizeNeteaseSongId(seedSongId);
        var currentSongId = NormalizeNeteaseSongId(state.NowPlayingProviderItem?.ActualId);
        var randomSongId = seedSongIds[RandomNumberGenerator.GetInt32(seedSongIds.Count)];
        var seedSong = !string.IsNullOrWhiteSpace(seedSongId) ? seedSongId
            : !string.IsNullOrWhiteSpace(currentSongId) && seedSongIds.Contains(currentSongId) ? currentSongId
            : randomSongId;
        var requestCount = System.Math.Max(seedSongIds.Count, seedPlaylist.TrackCount);

        try
        {
            var songs = await GetIntelligenceSongsAsync(neteaseProvider, seedPlaylist.ActualId, seedSong, requestCount, cancellationToken);
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
        global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
        string playlistId,
        string seedSong,
        int count,
        CancellationToken cancellationToken)
    {
        var result = await neteaseProvider.RequestAsync(
            NeteaseApis.PlaymodeIntelligenceListApi,
            new PlaymodeIntelligenceListRequest
            {
                PlaylistId = playlistId,
                SongId = seedSong,
                StartMusicId = seedSong,
                Count = count
            },
            cancellationToken);

        return result.Match(
            success => success.Data?
                           .Select(item => item.SongInfo)
                           .Where(song => song is not null)
                           .Select(song => (SingleSongBase)song!.MapToNeteaseMusic())
                           .ToList() ?? [],
            _ => []);
    }

    private static async Task<List<string>> GetPlaylistSongIdsAsync(
        NeteasePlaylist playlist,
        CancellationToken cancellationToken)
    {
        var count = playlist.TrackCount > 0
            ? System.Math.Min(playlist.TrackCount, playlist.MaxProgressiveCount)
            : playlist.MaxProgressiveCount;
        var (_, items) = await playlist.GetProgressiveItemsListAsync(0, count, cancellationToken);
        return items.OfType<SingleSongBase>()
            .Select(song => NormalizeNeteaseSongId(song.ActualId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    private static async Task<NeteasePlaylist?> ResolveLikedMusicPlaylistAsync(
        global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
        IAuthService auth,
        string? playlistId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(playlistId))
        {
            var playlist = auth.MySongLists.FirstOrDefault(pl => pl.ActualId == playlistId);
            if (playlist is not null)
                return playlist;

            return await neteaseProvider.GetPlaylistById(playlistId, cancellationToken);
        }

        var cached = FindLikedMusicPlaylist(auth.MySongLists);
        if (cached is not null)
            return cached;

        if (auth.CurrentUser is null)
            return null;

        var containers = await auth.CurrentUser.GetSubContainerAsync(cancellationToken);
        return FindLikedMusicPlaylist(containers);
    }

    private static NeteasePlaylist? FindLikedMusicPlaylist(IEnumerable<ContainerBase> containers)
    {
        var playlistContainers = containers.OfType<NeteaseUserPlaylistSubContainer>().ToList();
        var createdPlaylists = playlistContainers
            .Where(container => container.Name.Contains("创建", System.StringComparison.Ordinal))
            .SelectMany(container => container.Playlists)
            .ToList();

        if (createdPlaylists.Count > 0)
            return createdPlaylists[0];

        return containers.OfType<NeteasePlaylist>()
            .Where(playlist => !playlist.Subscribed)
            .FirstOrDefault();
    }

    private static string? NormalizeNeteaseSongId(string? songId)
    {
        return songId?.StartsWith(NeteaseTypeIds.SingleSong) is true ? songId[2..] : songId;
    }
}
