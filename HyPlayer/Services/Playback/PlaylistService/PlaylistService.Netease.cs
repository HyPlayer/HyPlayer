using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
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
    public HyPlayItem NCSongToPlayItem(NCSong ncSong)
    {
        return ncSong.ToHyPlayItem();
    }

    /// <inheritdoc />
    public HyPlayItem AppendNcSong(NCSong ncSong, int position = -1)
    {
        var hpi = NCSongToPlayItem(ncSong);
        lock (_lock)
        {
            if (_items.Contains(hpi))
                return hpi;

            if (position < 0 || position >= _items.Count)
                _items.Add(hpi);
            else
                _items.Insert(position, hpi);
        }

        NotifyAppendDone();
        return hpi;
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
                var hpi = NCSongToPlayItem(ncSong);
                lock (_lock) { _items.Add(hpi); }
            }

            NotifyAppendDone();
        }
        catch (Exception ex)
        {
            _teachingTipService.Items.Enqueue(new("AppendNCSong时发生错误", ex.Message));
        }
    }

    /// <inheritdoc />
    public List<HyPlayItem> AppendNcSongRange(List<NCSong> ncSongs, int position = -1)
    {
        lock (_lock)
        {
            if (position < 0)
                position = _items.Count;

            var insertList = ncSongs.Select(NCSongToPlayItem)
                .Where(t => !_items.Contains(t))
                .ToList();

            if (insertList.Count <= 0)
                return insertList;

            _items.InsertRange(position, insertList);
            NotifyAppendDone();
            return insertList;
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

                    foreach (var ncSong in batch)
                    {
                        if (result.Batches.Count == 1 && batch.Count == 1)
                        {
                            var singleItem = NCSongToPlayItem(ncSong);
                            if (_items.Contains(singleItem))
                                continue;

                            _items.Add(singleItem);
                        }
                        else
                        {
                            _items.Add(NCSongToPlayItem(ncSong));
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
            _teachingTipService.Items.Enqueue(new("AppendNCSong时发生错误", ex.Message));
        }
    }
}
