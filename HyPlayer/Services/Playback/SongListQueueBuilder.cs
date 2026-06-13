using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

internal sealed class SongListQueueBuilder(
    PlayCoreBase playCore,
    IPlaybackQueueLoader queueLoader,
    IPlaybackControlService control,
    PlaybackStateService state,
    INotificationService notification) : ISongListQueueBuilder
{
    public async Task BuildAndPlayAsync(SingleSongBase clickedSong, SongListQueueScope scope, IReadOnlyList<SingleSongBase> visibleSongs)
    {
        if (visibleSongs.Count == 0) return;

        var currentSongId = state.NowPlayingProviderItem?.ActualId;
        var shiftSong = clickedSong.ActualId == currentSongId && state.IsPlaying;
        var nowPlayingIndex = await playCore.GetCurrentIndexAsync().ConfigureAwait(false);

        if (scope.CanLoadCompleteSource)
        {
            await AppendCompleteSourceAsync(scope, visibleSongs.Count, !shiftSong);
        }
        else
        {
            if (!shiftSong)
                await playCore.StopAsync().ConfigureAwait(false);

            await playCore.RemoveAllSongAsync().ConfigureAwait(false);
            await playCore.InsertSongRangeAsync(visibleSongs.ToList()).ConfigureAwait(false);
        }

        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null)
            playCore.PlaySourceId = playSourceId;

        if (!shiftSong)
        {
            await playCore.MovePointerToAsync(clickedSong).ConfigureAwait(false);
            if (playCore.CurrentSong is { } song)
                await control.LoadAndPlayAsync(song, removeCurrentSongs: false).ConfigureAwait(false);
            return;
        }

        notification.ShowMessage("无感歌单切换", "成功无感切换到歌单");
        if (nowPlayingIndex >= 0)
        {
            await playCore.MovePointerToIndexAsync(nowPlayingIndex).ConfigureAwait(false);
        }
    }

    private async Task AppendCompleteSourceAsync(SongListQueueScope scope, int availableVisibleCount, bool clearFirst)
    {
        if (scope.Id == null) return;

        // Kind-based 路由 — 跳过 string 编码/解码，直接委托给 IQueueSourceProvider
        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null
            && playCore.PlaySourceId == playSourceId
            && (await playCore.GetPlaylistAsync().ConfigureAwait(false)).Count == availableVisibleCount)
            return;

        if (clearFirst)
        {
            await playCore.StopAsync().ConfigureAwait(false);
            await playCore.RemoveAllSongAsync().ConfigureAwait(false);
        }

        await queueLoader.AppendSourceByKindAsync(scope.Kind, scope.Id).ConfigureAwait(false);
    }
}
