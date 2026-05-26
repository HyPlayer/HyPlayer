using HyPlayer.Domain.Music;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

internal sealed class SongListQueueBuilder(
    IPlaylistService playlist,
    PlaybackStateService state,
    INotificationService notification) : ISongListQueueBuilder
{
    public async Task BuildAndPlayAsync(NCSong clickedSong, SongListQueueScope scope, IReadOnlyList<NCSong> visibleSongs)
    {
        if (visibleSongs.Count == 0) return;

        var currentSongId = state.NowPlayingProviderItem?.ActualId;
        var shiftSong = clickedSong.SongId == currentSongId && state.IsPlaying;
        var nowPlaying = state.NowPlayingItem;

        if (!clickedSong.IsAvailable)
        {
            notification.ShowMessage("歌曲不可用", $"歌曲 {clickedSong.SongName} 当前不可用");
            return;
        }

        if (scope.CanLoadCompleteSource)
        {
            await AppendCompleteSourceAsync(scope, visibleSongs, !shiftSong);
        }
        else
        {
            playlist.Clear(!shiftSong);
            if (visibleSongs.All(song => song.ProviderSong != null))
                playlist.AppendItems(visibleSongs.Select(song => song.ProviderSong));
            else
                playlist.AppendNcSongs(visibleSongs.ToList());
        }

        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null)
            playlist.PlaySourceId = playSourceId;

        if (!shiftSong)
        {
            var targetItem = playlist.Items.FirstOrDefault(song => song?.Id == clickedSong.SongId);
            if (targetItem != null)
                await playlist.MoveToAsync(targetItem);
            return;
        }

        notification.ShowMessage("无感歌单切换", "成功无感切换到歌单");
        if (nowPlaying != null)
        {
            playlist.RestoreNowPlayingItem(nowPlaying);
            playlist.NotifyAppendDone();
        }
    }

    private async Task AppendCompleteSourceAsync(SongListQueueScope scope, IReadOnlyList<NCSong> visibleSongs, bool clearFirst)
    {
        if (scope.Id == null) return;

        // Kind-based 路由 — 跳过 string 编码/解码，直接委托给 IQueueSourceProvider
        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null
            && playlist.PlaySourceId == playSourceId
            && playlist.Items.Count == visibleSongs.Count(t => t.IsAvailable))
            return;

        playlist.Clear(clearFirst);
        await playlist.AppendSourceByKindAsync(scope.Kind, scope.Id);
    }
}
