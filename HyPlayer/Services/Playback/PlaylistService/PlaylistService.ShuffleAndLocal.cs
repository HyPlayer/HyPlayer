using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── Shuffle / 本地文件 / 通知 ──────────────

    /// <inheritdoc />
    public List<int> ShuffleList { get; } = [];

    /// <inheritdoc />
    public int ShufflingIndex { get; set; } = -1;

    /// <inheritdoc />
    public void CreateShufflePlayLists()
    {
        ShuffleList.Clear();
        var snapshot = ProviderQueueSnapshot;
        var currentSongId = NowPlayingProviderItem?.ActualId ?? "-1";
        if (snapshot.Count != 0)
        {
            HashSet<int> shuffledNumbers = [];
            if (currentSongId != "-1")
            {
                int playItemIndex = snapshot.ToList().FindIndex(s => s?.ActualId == currentSongId);
                if (playItemIndex != -1)
                {
                    shuffledNumbers.Add(playItemIndex);
                    ShuffleList.Add(playItemIndex);
                }
            }

            while (shuffledNumbers.Count < snapshot.Count)
            {
                var indexShuffled = RandomNumberGenerator.GetInt32(snapshot.Count);
                if (shuffledNumbers.Add(indexShuffled))
                    ShuffleList.Add(indexShuffled);
            }
        }

        SendPlaylistChanged(true);
    }

    /// <inheritdoc />
    public void RestoreNowPlayingIndex(int index)
    {
        RunSynchronously(MoveToIndexAsync(index));
    }

    /// <inheritdoc />
    public void ReverseList()
    {
        RunSynchronously(ReverseListAsync());
    }

    private async System.Threading.Tasks.Task ReverseListAsync()
    {
        var snapshot = ProviderItems.Reverse().ToList();
        var current = NowPlayingProviderItem;

        await _playCore.RemoveAllSongAsync().ConfigureAwait(false);
        await _playCore.InsertSongRangeAsync(snapshot).ConfigureAwait(false);

        if (current is not null)
            await _playCore.MovePointerToAsync(current).ConfigureAwait(false);

        await RefreshAfterPlaylistChangedAsync().ConfigureAwait(false);
    }
}
