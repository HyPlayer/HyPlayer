using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.User;
using HyPlayer.Features.Video;
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
    public HyPlayItem NowPlayingItem => state.NowPlayingItem;

    public SingleSongBase? NowPlayingProviderItem => state.NowPlayingProviderItem;

    public async Task PlayNowAsync(IReadOnlyList<NCSong> selectedSongs, NCSong selectedSong)
    {
        if (selectedSongs.Count == 0) return;
        if (!selectedSong.IsAvailable)
        {
            notification.ShowMessage("歌曲不可用", $"歌曲 {selectedSong.SongName} 当前不可用");
            return;
        }

        foreach (var ncsong in selectedSongs)
        {
            if (ncsong.ProviderSong != null)
                playlist.AppendItem(ncsong.ProviderSong);
            else
                playlist.AppendNcSong(ncsong);
        }

        if (selectedSong.ProviderSong is not null)
        {
            await playlist.MoveToAsync(selectedSong.ProviderSong);
        }
        else
        {
            var targetIndex = playlist.Items.ToList().FindIndex(t => t.Id == selectedSong.SongId);
            if (targetIndex >= 0)
                await playlist.MoveToIndexAsync(targetIndex);
        }
    }

    public void AddToNext(IReadOnlyList<NCSong> selectedSongs, NCSong selectedSong)
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

    public async Task OpenSingerAsync(NCSong selectedSong)
    {
        if (selectedSong.Artist == null || selectedSong.Artist.Count == 0) return;

        if (selectedSong.Artist[0].Type == HyPlayItemType.Radio)
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

    public void OpenAlbum(NCSong selectedSong)
    {
        navigation.Navigate(typeof(AlbumPage), selectedSong.Album.Id ?? "");
    }

    public void OpenComments(NCSong selectedSong)
    {
        navigation.Navigate(typeof(Comments), CommentTarget.Song(selectedSong.SongId));
    }

    public void DownloadSongs(IEnumerable<NCSong> selectedSongs)
    {
        foreach (var ncsong in selectedSongs)
        {
            if (ncsong.ProviderSong != null)
                DownloadManager.AddDownload(ncsong.ProviderSong);
            else
                DownloadManager.AddDownload(ncsong);
        }
    }

    private List<int> AppendToNext(IReadOnlyList<NCSong> selectedSongs)
    {
        if (selectedSongs.All(song => song.ProviderSong != null))
            return AppendProviderSongs(selectedSongs.Select(song => song.ProviderSong!).ToList(), playlist.NowPlayingIndex + 1);

        return playlist.AppendNcSongRange([.. selectedSongs], playlist.NowPlayingIndex + 1);
    }

    private List<int> AppendProviderSongs(IReadOnlyList<SingleSongBase> providerSongs, int position)
    {
        var insertedIndexes = new List<int>();
        for (var offset = 0; offset < providerSongs.Count; offset++)
        {
            var targetIndex = position + offset;
            playlist.AppendItem(providerSongs[offset], targetIndex);
            insertedIndexes.Add(targetIndex);
        }

        return insertedIndexes;
    }

    public void OpenMv(NCSong selectedSong)
    {
        navigation.Navigate(typeof(MVPage), selectedSong);
    }

    public async Task CollectAsync(NCSong selectedSong)
    {
        await new SongListSelect(selectedSong.SongId).ShowAsync();
    }

    internal async Task PlayClickedSongAsync(NCSong clickedSong, SongListQueueScope scope, IReadOnlyList<NCSong> visibleSongs)
    {
        await queueBuilder.BuildAndPlayAsync(clickedSong, scope, visibleSongs);
    }

}
