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
            List<SingleSongBase?> snapshot;
            int nowPlayingIndex;

            lock (_lock)
            {
                snapshot = _providerItems.ToList();
                nowPlayingIndex = _nowPlayingIndex;
            }

            var converted = snapshot
                .Where(item => item is not null)
                .Cast<SingleSongBase>()
                .ToList();

            // Temporary shadow sync: only provider-backed queue entries are mirrored to PlayCore.
            // Local files still rely on the legacy HyPlayItem/PlayItem boundary.
            if (converted.Count == 0)
                return;

            if (_playCore.CurrentPlayList is null)
                return;

            await _playCore.CurrentPlayList.SetSongListAsync(converted).ConfigureAwait(false);

            var currentSong = nowPlayingIndex >= 0 && nowPlayingIndex < snapshot.Count
                ? snapshot[nowPlayingIndex]
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
