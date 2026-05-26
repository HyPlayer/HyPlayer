using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
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
                            if (ContainsProviderItem(providerSong))
                                continue;

                            InsertQueueItem(providerSong);
                        }
                        else
                        {
                            InsertQueueItem(providerSong);
                        }

                        hasChanges = true;
                    }
                }
            }

            if (hasChanges)
                PublishPlaylistChanged();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    private bool ContainsProviderItem(SingleSongBase song)
    {
        return _providerItems.Any(providerItem => providerItem is not null
            && providerItem.ProviderId == song.ProviderId
            && providerItem.TypeId == song.TypeId
            && providerItem.ActualId == song.ActualId);
    }
}
