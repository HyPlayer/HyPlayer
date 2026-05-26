using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── NCSong 相关 ──────────────

    /// <inheritdoc />
    public void AppendNcSong(NCSong ncSong, int position = -1)
    {
        var providerSong = ncSong.ToProviderSong();
        var hpi = ToLegacyQueueItem(providerSong);
        lock (_lock)
        {
            if (_items.Contains(hpi))
                return;

            InsertQueueItem(hpi, providerSong, position);
        }

        NotifyAppendDone();
    }

    /// <inheritdoc />
    public void AppendNcSongs(IList<NCSong> ncSongs, bool clearFirst = true)
    {
        if (ncSongs == null) return;
        try
        {
            if (clearFirst)
            {
                ExitPersonalFmForSourceChange();
                lock (_lock)
                {
                    Clear(clearFirst);
                }
            }

            foreach (var ncSong in ncSongs)
            {
                var providerSong = ncSong.ToProviderSong();
                var hpi = ToLegacyQueueItem(providerSong);
                lock (_lock) { InsertQueueItem(hpi, providerSong); }
            }

            NotifyAppendDone();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    /// <inheritdoc />
    public List<int> AppendNcSongRange(List<NCSong> ncSongs, int position = -1)
    {
        lock (_lock)
        {
            if (position < 0)
                position = _items.Count;

            var providerSongs = ncSongs.Select(song => song.ToProviderSong()).ToList();
            var insertList = providerSongs.Select(ToLegacyQueueItem)
                .Where(t => !_items.Contains(t))
                .ToList();

            if (insertList.Count <= 0)
                return [];

            var insertedIndexes = new List<int>();

            foreach (var (providerSong, index) in providerSongs.Zip(Enumerable.Range(0, providerSongs.Count)))
            {
                var item = ToLegacyQueueItem(providerSong);
                if (_items.Contains(item))
                    continue;

                var targetIndex = position + index;
                InsertQueueItem(item, providerSong, targetIndex);
                insertedIndexes.Add(targetIndex);
            }
            NotifyAppendDone();
            return insertedIndexes;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AppendNcSourceAsync(string sourceId)
    {
        if (sourceId.Length < 3)
            return false;

        var prefix = sourceId[..2];
        var id = sourceId[2..];

        if (!_providersByPrefix.TryGetValue(prefix, out var provider))
            return false;

        var result = await provider.LoadAsync(id);
        AppendNcSongBatches(result);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> AppendSourceByKindAsync(SongListQueueScopeKind kind, string id)
    {
        if (!_providersByKind.TryGetValue(kind, out var provider))
            return false;

        var result = await provider.LoadAsync(id);
        AppendNcSongBatches(result);
        return result.Success;
    }

    /// <inheritdoc />
    public Task<bool> AppendPlayListAsync(string playlistId)
        => AppendSourceByKindAsync(SongListQueueScopeKind.Playlist, playlistId);

    /// <inheritdoc />
    public async Task<bool> AppendRadioListAsync(string radioId, bool asc = false)
    {
        if (_providersByKind.TryGetValue(SongListQueueScopeKind.Radio, out var provider)
            && provider is QueueProviders.RadioQueueSourceProvider radioProvider)
        {
            var result = await radioProvider.LoadAsync(radioId, asc);
            AppendNcSongBatches(result);
            return result.Success;
        }
        return false;
    }

    private async Task<bool> AppendSingerHotAsync(string id)
        => await AppendSourceByKindAsync(SongListQueueScopeKind.Artist, id);

    private async Task<bool> AppendAlbumAsync(string albumId)
        => await AppendSourceByKindAsync(SongListQueueScopeKind.Album, albumId);

    private void AppendNcSongBatches(NeteaseQueueSourceLoadResult result)
    {
        if (!result.Success)
            return;

        try
        {
            var hasChanges = false;
            lock (_lock)
            {
                foreach (var batch in result.Batches)
                {
                    if (batch is not { Count: > 0 })
                        continue;

                    foreach (var providerSong in batch)
                    {
                        if (result.Batches.Count == 1 && batch.Count == 1)
                        {
                            var singleItem = ToLegacyQueueItem(providerSong);
                            if (_items.Contains(singleItem))
                                continue;

                            InsertQueueItem(singleItem, providerSong);
                        }
                        else
                        {
                            InsertQueueItem(ToLegacyQueueItem(providerSong), providerSong);
                        }

                        hasChanges = true;
                    }
                }
            }

            if (hasChanges)
                NotifyAppendDone();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    private HyPlayItem ToLegacyQueueItem(SingleSongBase song)
    {
        return FindLegacyQueueItem(song) ?? HyPlayItem.FromProviderSong(song);
    }
}
