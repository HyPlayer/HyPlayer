using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.Storage;

namespace HyPlayer.Services.Playback;

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
    public void AppendNcSongs(IList<NCSong> ncSongs, bool clearFirst = true, string currentSongId = "-1")
    {
        if (ncSongs == null) return;
        try
        {
            if (clearFirst)
            {
                ExitPersonalFmForSourceChange();
                lock (_lock)
                {
                    DisposePlayItems(_items);
                    _items.Clear();
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
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
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
        var result = await _neteaseQueueSource.LoadSourceAsync(sourceId);
        AppendNcSongBatches(result);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> AppendPlayListAsync(string playlistId)
    {
        var result = await _neteaseQueueSource.LoadPlaylistAsync(playlistId);
        AppendNcSongBatches(result);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> AppendRadioListAsync(string radioId, bool asc = false)
    {
        var result = await _neteaseQueueSource.LoadRadioListAsync(radioId, asc);
        AppendNcSongBatches(result);
        return result.Success;
    }

    private async Task<bool> AppendSingerHotAsync(string id)
    {
        var result = await _neteaseQueueSource.LoadSingerHotAsync(id);
        AppendNcSongBatches(result);
        return result.Success;
    }

    private async Task<bool> AppendAlbumAsync(string albumId)
    {
        var result = await _neteaseQueueSource.LoadAlbumAsync(albumId);
        AppendNcSongBatches(result);
        return result.Success;
    }

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
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }
}
