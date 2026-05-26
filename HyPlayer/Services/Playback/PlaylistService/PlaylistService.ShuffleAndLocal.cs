using HyPlayer.Domain.Music;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── Shuffle / 本地文件 / 通知 ──────────────

    /// <inheritdoc />
    public List<int> ShuffleList { get; } = [];

    /// <inheritdoc />
    public int ShufflingIndex { get; set; } = -1;

    /// <inheritdoc />
    public async Task PickLocalFileAsync()
    {
        var items = await _localFileImport.PickLocalFilesAsync();
        if (items.Count == 0) return;

        lock (_lock)
        {
            InsertQueueItems(items);
        }

        NotifyAppendDone();
        HyPlayItem? lastItem;
        lock (_lock) { lastItem = _items.LastOrDefault(); }
        if (lastItem != null)
            await MoveToAsync(lastItem);
    }

    /// <inheritdoc />
    public async Task<HyPlayItem> LoadStorageFileAsync(StorageFile sf, bool nocheck163 = false)
    {
        return await _localFileImport.LoadStorageFileAsync(sf, nocheck163);
    }

    /// <inheritdoc />
    public void CreateShufflePlayLists()
    {
        ShuffleList.Clear();
        var currentSongId = NowPlayingItem?.Id ?? "-1";
        lock (_lock)
        {
            if (_items.Count != 0)
            {
                HashSet<int> shuffledNumbers = [];
                if (currentSongId != "-1")
                {
                    int playItemIndex = _items.FindIndex(s => s.GetItemIdentity().ActualId == currentSongId);
                    if (playItemIndex != -1)
                    {
                        shuffledNumbers.Add(playItemIndex);
                        ShuffleList.Add(playItemIndex);
                    }
                }

                while (shuffledNumbers.Count < _items.Count)
                {
                    var indexShuffled = RandomNumberGenerator.GetInt32(_items.Count);
                    if (shuffledNumbers.Add(indexShuffled))
                        ShuffleList.Add(indexShuffled);
                }
            }
        }

        SendPlaylistChanged(true);
    }

    /// <inheritdoc />
    public void RestoreNowPlayingItem(HyPlayItem item)
    {
        lock (_lock)
        {
            var index = _items.IndexOf(item);
            if (index < 0 || ReferenceEquals(item, NowPlayingItem))
                return;
            _items[index] = item;
            _providerItems[index] = NowPlayingProviderItem;
            _nowPlayingIndex = index;
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();
    }

    /// <inheritdoc />
    public void ReverseList()
    {
        lock (_lock)
        {
            _items.Reverse();
            _providerItems.Reverse();
            if (_nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count)
                _nowPlayingIndex = _items.Count - _nowPlayingIndex - 1;
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();
    }

    /// <summary>
    /// Disposes the internal <see cref="CancellationTokenSource"/> used for track-end handling.
    /// </summary>
    public void Dispose()
    {
        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = null;
        DisposePlayItems(_items);
        _items.Clear();
        _providerItems.Clear();
        _state.ClearNowPlaying();
        _trackEndLock.Dispose();
    }
}
