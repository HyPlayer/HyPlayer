#region

using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace HyPlayer.Classes;

internal class Api
{
    public static async Task<bool> LikeSong(string songid, bool like)
    {
        var requestResult = await Common.NeteaseAPI.RequestAsync(NeteaseApis.LikeApi,
            new LikeRequest() { TrackId = songid, Like = like, UserId = Common.LoginedUser.Id });
        if (requestResult.IsSuccess)
        {
            return true;
        }
        else
        {
            Common.AddToTeachingTipLists(requestResult.Error.Message);
            return false;
        }
    }

    public static async Task EnterIntelligencePlay(CancellationToken cancellationToken = default)
    {
        var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        var _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        _playlist.Clear();
        var songList = Common.MySongLists[0].PlaylistId;
        var randomSong = Common.LikedSongs[new Random().Next(0, Common.LikedSongs.Count - 1)];
        var jsoon = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaymodeIntelligenceListApi,
            new PlaymodeIntelligenceListRequest
            {
                PlaylistId = songList,
                SongId = randomSong,
                StartMusicId = _state.NowPlayingItem?.Id ?? randomSong,
                Count = Common.LikedSongs.Count
            }, cancellationToken);

        if (jsoon.IsError)
        {
            Common.AddToTeachingTipLists("加载心动模式列表出错", jsoon.Error.Message);
            return;
        }

        foreach (var item in jsoon.Value?.Data ?? [])
        {
            if (item.SongInfo is null) continue;
            var ncSong = item.SongInfo.MapNcSong();
            var playItem = _playlist.NCSongToPlayItem(ncSong);
            playItem.InfoTag = item.Recommended ? "为你推荐" : "我的喜欢";
            _playlist.AppendItem(playItem);
            _playlist.NotifyAppendDone();
            await _playlist.MoveToAsync(_playlist.Items.FirstOrDefault());

        }
    }
}