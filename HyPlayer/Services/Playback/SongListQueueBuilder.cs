using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

internal sealed class SongListQueueBuilder(
    IPlaylistService playlist,
    PlaybackStateService state,
    INotificationService notification) : ISongListQueueBuilder
{
    public async Task BuildAndPlayAsync(SingleSongBase clickedSong, SongListQueueScope scope, IReadOnlyList<SingleSongBase> visibleSongs)
    {
        if (visibleSongs.Count == 0) return;

        var currentSongId = state.NowPlayingProviderItem?.ActualId;
        var shiftSong = clickedSong.ActualId == currentSongId && state.IsPlaying;
        var nowPlayingIndex = playlist.NowPlayingIndex;

        if (scope.CanLoadCompleteSource)
        {
            await AppendCompleteSourceAsync(scope, visibleSongs.Count, !shiftSong);
        }
        else
        {
            playlist.Clear(!shiftSong);
            playlist.AppendItems(visibleSongs);
        }

        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null)
            playlist.PlaySourceId = playSourceId;

        if (!shiftSong)
        {
            await playlist.MoveToAsync(clickedSong);
            return;
        }

        notification.ShowMessage("无感歌单切换", "成功无感切换到歌单");
        if (nowPlayingIndex >= 0)
        {
            playlist.RestoreNowPlayingIndex(nowPlayingIndex);
        }
    }

    private async Task AppendCompleteSourceAsync(SongListQueueScope scope, int availableVisibleCount, bool clearFirst)
    {
        if (scope.Id == null) return;

        // Kind-based 路由 — 跳过 string 编码/解码，直接委托给 IQueueSourceProvider
        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null
            && playlist.PlaySourceId == playSourceId
            && playlist.QueueCount == availableVisibleCount)
            return;

        playlist.Clear(clearFirst);
        await playlist.AppendSourceByKindAsync(scope.Kind, scope.Id);
    }
}
