using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.User;
using HyPlayer.Features.Video;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using HyPlayer.Services.Playback;
using HyPlayer.UI.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists;

public partial class GroupedSongsListViewModel(
    IPlaylistService playlist,
    ISongListQueueBuilder queueBuilder,
    PlaybackStateService state,
    INotificationService notification,
    INavigationService navigation)
{
    public SingleSongBase? NowPlayingProviderItem => state.NowPlayingProviderItem;

    public async Task PlayNowAsync(IReadOnlyList<SongListItemViewModel> selectedSongs, SongListItemViewModel selectedSong)
    {
        if (selectedSongs.Count == 0) return;
        if (!selectedSong.IsAvailable)
        {
            notification.ShowMessage("歌曲不可用", $"歌曲 {selectedSong.SongName} 当前不可用");
            return;
        }

        foreach (var song in selectedSongs)
        {
            playlist.AppendItem(song.ToProviderSong());
        }

        if (selectedSong.ProviderSong is not null)
        {
            await playlist.MoveToAsync(selectedSong.ProviderSong);
        }
        else
        {
            var targetIndex = playlist.ProviderQueueSnapshot.ToList()
                .FindIndex(t => t?.ActualId == selectedSong.SongId);
            if (targetIndex >= 0)
                await playlist.MoveToIndexAsync(targetIndex);
        }
    }

    public void AddToNext(IReadOnlyList<SongListItemViewModel> selectedSongs, SongListItemViewModel selectedSong)
    {
        if (selectedSongs.Count == 0) return;
        if (!selectedSong.IsAvailable)
        {
            notification.ShowMessage("歌曲不可用", $"歌曲 {selectedSong.SongName} 当前不可用");
            return;
        }

        var playItemIndexes = AppendToNext(selectedSongs);
        if (state.ActiveStrategyId == "shn")
        {
            for (int i = 0; i < playItemIndexes.Count; i++)
            {
                var item = playItemIndexes[i];
                var currentIndex = playlist.ShuffleList.IndexOf(playlist.NowPlayingIndex);
                var nextIndex = currentIndex + i + 1;
                if (nextIndex >= playlist.ShuffleList.Count) break;
                var targetIndex = playlist.ShuffleList.IndexOf(item);
                var targetItem = playlist.ShuffleList[nextIndex];
                playlist.ShuffleList[targetIndex] = targetItem;
                playlist.ShuffleList[nextIndex] = item;
            }
        }

        var unAvailableSongNames = selectedSongs.Where(t => !t.IsAvailable).Select(t => t.SongName).ToArray();
        if (unAvailableSongNames.Length > 0)
        {
            notification.ShowMessage("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
    }

    public async Task OpenSingerAsync(SongListItemViewModel selectedSong)
    {
        if (selectedSong.Artist == null || selectedSong.Artist.Count == 0) return;

        if (selectedSong.IsRadio)
        {
            navigation.Navigate(typeof(Me), selectedSong.Artist[0].Id ?? "");
        }
        else
        {
            if (selectedSong is { Artist.Count: > 1 })
                await new ArtistSelectDialog(selectedSong.Artist).ShowAsync();
            else
                navigation.Navigate(typeof(ArtistPage), selectedSong.Artist[0].Id ?? "");
        }
    }

    public void OpenAlbum(SongListItemViewModel selectedSong)
    {
        navigation.Navigate(typeof(AlbumPage), selectedSong.Album.Id ?? "");
    }

    public void OpenComments(SongListItemViewModel selectedSong)
    {
        navigation.Navigate(typeof(Comments), CommentTarget.Song(selectedSong.SongId));
    }

    public void DownloadSongs(IEnumerable<SongListItemViewModel> selectedSongs)
    {
        DownloadManager.AddDownload(selectedSongs
            .Select(song => song.ToProviderSong())
            .ToList());
    }

    private List<int> AppendToNext(IReadOnlyList<SongListItemViewModel> selectedSongs)
    {
        return playlist.AppendItems(
            selectedSongs.Select(song => song.ToProviderSong()).ToList(),
            playlist.NowPlayingIndex + 1);
    }

    public void OpenMv(SongListItemViewModel selectedSong)
    {
        navigation.Navigate(typeof(MVPage), selectedSong);
    }

    public async Task CollectAsync(SongListItemViewModel selectedSong)
    {
        await new SongListSelect(selectedSong.SongId).ShowAsync();
    }

    internal async Task PlayClickedSongAsync(SongListItemViewModel clickedSong, SongListQueueScope scope, IReadOnlyList<SongListItemViewModel> visibleSongs)
    {
        await queueBuilder.BuildAndPlayAsync(
            clickedSong.ToProviderSong(),
            scope,
            visibleSongs.Select(song => song.ToProviderSong()).ToList());
    }

}
