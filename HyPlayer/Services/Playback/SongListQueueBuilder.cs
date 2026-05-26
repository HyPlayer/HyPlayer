using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
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
        var nowPlayingIndex = playlist.NowPlayingIndex;

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
            playlist.AppendItems(visibleSongs.Select(song => song.ProviderSong ?? song.ToProviderSong()));
            playlist.NotifyAppendDone();
        }

        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null)
            playlist.PlaySourceId = playSourceId;

        if (!shiftSong)
        {
            if (clickedSong.ProviderSong is not null)
            {
                await playlist.MoveToAsync(clickedSong.ProviderSong);
                return;
            }

            var targetIndex = playlist.ProviderQueueSnapshot.ToList()
                .FindIndex(song => song?.ActualId == clickedSong.SongId);
            if (targetIndex >= 0)
                await playlist.MoveToIndexAsync(targetIndex);
            return;
        }

        notification.ShowMessage("无感歌单切换", "成功无感切换到歌单");
        if (nowPlayingIndex >= 0)
        {
            playlist.RestoreNowPlayingIndex(nowPlayingIndex);
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
            && playlist.QueueCount == visibleSongs.Count(t => t.IsAvailable))
            return;

        playlist.Clear(clearFirst);
        await playlist.AppendSourceByKindAsync(scope.Kind, scope.Id);
    }
}
