#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.NeteaseProvider.Models;
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

    public static async Task EnterIntelligencePlay(CancellationToken cancellationToken = default)
    {
        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        var neteaseProvider = Ioc.Default.GetRequiredService<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
        var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        var state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        var auth = Ioc.Default.GetRequiredService<IAuthService>();
        playlist.Clear();

        var likedSongs = auth.LikedSongs;
        if (likedSongs.Count == 0)
        {
            notification.ShowMessage("无法进入心动模式", "当前账号还没有喜欢的歌曲");
            return;
        }

        var randomSong = likedSongs[RandomNumberGenerator.GetInt32(likedSongs.Count)];
        var seedSong = state.NowPlayingProviderItem?.ActualId ?? randomSong;

        try
        {
            var recommendationContainer = new NeteaseContextRecommendationContainer
            {
                ActualId = seedSong,
                SeedItemId = seedSong,
                Name = "相关推荐",
                Count = likedSongs.Count
            };

            var songs = await GetContainerSongsAsync(recommendationContainer, likedSongs.Count, cancellationToken);
            foreach (var song in songs)
            {
                playlist.AppendItem(song);
                playlist.SetItemInfoTag(song, likedSongs.Contains(song.ActualId ?? string.Empty) ? "我的喜欢" : "为你推荐");
            }

            playlist.NotifyAppendDone();
            if (playlist.Items.Count > 0)
                await playlist.MoveToAsync(playlist.Items[0]);
        }
        catch (System.Exception ex)
        {
            notification.ShowMessage("加载心动模式列表出错", ex.Message);
        }
    }

    private static async Task<List<SingleSongBase>> GetContainerSongsAsync(
        ContainerBase container,
        int count,
        CancellationToken cancellationToken)
    {
        List<ProvidableItemBase> items = container switch
        {
            LinerContainerBase linerContainer => await linerContainer.GetAllItemsAsync(cancellationToken),
            IProgressiveLoadingContainer progressiveContainer =>
                (await progressiveContainer.GetProgressiveItemsListAsync(0, count, cancellationToken)).Item2,
            UndeterminedContainerBase undeterminedContainer => await undeterminedContainer.GetNextItemsRangeAsync(cancellationToken),
            _ => []
        };

        return items.OfType<SingleSongBase>().ToList();
    }
}
