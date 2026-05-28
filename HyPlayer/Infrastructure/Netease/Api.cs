#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
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
        var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        var teachingTipService = Ioc.Default.GetRequiredService<ITeachingTipService>();
        var requestResult = await api.RequestAsync(NeteaseApis.LikeApi,
            new LikeRequest() { TrackId = songid, Like = like, UserId = Ioc.Default.GetRequiredService<IAuthService>().CurrentUser.Id });
        if (requestResult.IsSuccess)
        {
            return true;
        }
        else
        {
            teachingTipService.Items.Enqueue(new("红心歌曲时发生错误", requestResult.Error.Message));
            return false;
        }
    }

    public static async Task EnterIntelligencePlay(CancellationToken cancellationToken = default)
    {
        var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        var teachingTipService = Ioc.Default.GetRequiredService<ITeachingTipService>();
        var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        var state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        playlist.Clear();
        var songList = Ioc.Default.GetRequiredService<IAuthService>().MySongLists[0].PlaylistId;
        var likedSongs = Ioc.Default.GetRequiredService<IAuthService>().LikedSongs;
        var randomSong = likedSongs[RandomNumberGenerator.GetInt32(likedSongs.Count)];
        var jsoon = await api.RequestAsync(NeteaseApis.PlaymodeIntelligenceListApi,
            new PlaymodeIntelligenceListRequest
            {
                PlaylistId = songList,
                SongId = randomSong,
                StartMusicId = state.NowPlayingItem?.Id ?? randomSong,
                Count = likedSongs.Count
            }, cancellationToken);

        if (jsoon.IsError)
        {
            teachingTipService.Items.Enqueue(new("加载心动模式列表出错", jsoon.Error.Message));
            return;
        }

        foreach (var item in jsoon.Value?.Data ?? [])
        {
            if (item.SongInfo is null) continue;
            var ncSong = item.SongInfo.MapNcSong();
            var playItem = ncSong.ToHyPlayItem();
            playItem.InfoTag = item.Recommended ? "为你推荐" : "我的喜欢";
            playlist.AppendItem(playItem);
        }
        playlist.NotifyAppendDone();
        await playlist.MoveToAsync(playlist.Items[0]);
    }
}
