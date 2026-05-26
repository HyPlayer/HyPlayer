using System.Collections.Generic;
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
        var currentSongId = NowPlayingProviderItem?.ActualId ?? "-1";
        lock (_lock)
        {
            if (_providerItems.Count != 0)
            {
                HashSet<int> shuffledNumbers = [];
                if (currentSongId != "-1")
                {
                    int playItemIndex = _providerItems.FindIndex(s => s?.ActualId == currentSongId);
                    if (playItemIndex != -1)
                    {
                        shuffledNumbers.Add(playItemIndex);
                        ShuffleList.Add(playItemIndex);
                    }
                }

                while (shuffledNumbers.Count < _providerItems.Count)
                {
                    var indexShuffled = RandomNumberGenerator.GetInt32(_providerItems.Count);
                    if (shuffledNumbers.Add(indexShuffled))
                        ShuffleList.Add(indexShuffled);
                }
            }
        }

        SendPlaylistChanged(true);
    }

    /// <inheritdoc />
    public void RestoreNowPlayingIndex(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _providerItems.Count || index == _nowPlayingIndex)
                return;
            _nowPlayingIndex = index;
            SyncIndex();
        }
        PublishPlaylistChanged();
    }

    /// <inheritdoc />
    public void ReverseList()
    {
        lock (_lock)
        {
            _providerItems.Reverse();
            if (_nowPlayingIndex >= 0 && _nowPlayingIndex < _providerItems.Count)
                _nowPlayingIndex = _providerItems.Count - _nowPlayingIndex - 1;
            SyncIndex();
        }
        PublishPlaylistChanged();
    }

    /// <summary>
    /// Disposes the internal <see cref="CancellationTokenSource"/> used for track-end handling.
    /// </summary>
    public void Dispose()
    {
        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = null;
        _providerItems.Clear();
        _state.ClearNowPlaying();
        _trackEndLock.Dispose();
    }
}
