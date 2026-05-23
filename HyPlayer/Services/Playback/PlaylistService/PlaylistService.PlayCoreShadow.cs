using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    private void SchedulePlayCorePlaylistSync()
    {
        _taskRunner.Forget(SyncPlayCorePlaylistAsync, "sync playlist to PlayCore");
    }

    private async Task SyncPlayCorePlaylistAsync()
    {
        try
        {
            List<HyPlayItem> snapshot;
            HyPlayItem? nowPlayingItem;
            int nowPlayingIndex;

            lock (_lock)
            {
                snapshot = _items.ToList();
                nowPlayingIndex = _nowPlayingIndex;
                nowPlayingItem = _nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count
                    ? _items[_nowPlayingIndex]
                    : null;
            }

            var converted = snapshot
                .Select(item => item.ToSingleSong())
                .Where(song => song is not null)
                .Cast<SingleSongBase>()
                .ToList();

            if (_playCore.CurrentPlayList is null)
                return;

            await _playCore.CurrentPlayList.SetSongListAsync(converted).ConfigureAwait(false);

            var currentSong = nowPlayingItem is not null && nowPlayingIndex >= 0 && nowPlayingIndex < converted.Count
                ? converted[nowPlayingIndex]
                : null;
            if (currentSong is not null && _playCore.CurrentPlayListController is not null)
                await _playCore.MovePointerToAsync(currentSong).ConfigureAwait(false);
        }
        catch
        {
            // PlayCore playlist sync must not break the temporary UI boundary.
        }
    }
}
