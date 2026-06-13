using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.QueueProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

public sealed class PlaybackQueueLoader : IPlaybackQueueLoader
{
    private readonly PlayCoreBase _playCore;
    private readonly INotificationService _notification;
    private readonly IReadOnlyDictionary<SongListQueueScopeKind, IQueueSourceProvider> _providersByKind;
    private readonly IReadOnlyDictionary<string, IQueueSourceProvider> _providersByPrefix;

    public PlaybackQueueLoader(
        PlayCoreBase playCore,
        IEnumerable<IQueueSourceProvider> queueSourceProviders,
        INotificationService notification)
    {
        _playCore = playCore;
        _notification = notification;

        var providerList = queueSourceProviders.ToList();
        _providersByKind = providerList.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.First());
        var byPrefix = providerList.ToDictionary(p => p.Prefix, StringComparer.Ordinal);
        if (byPrefix.TryGetValue(QueueSourcePrefixes.Singer, out var singerProvider))
            byPrefix[QueueSourcePrefixes.SingerAlias] = singerProvider;
        _providersByPrefix = byPrefix;
    }

    public async Task<bool> AppendNcSourceAsync(string sourceId)
    {
        if (sourceId.Length < 3)
            return false;

        var prefix = sourceId[..2];
        var id = sourceId[2..];

        if (!_providersByPrefix.TryGetValue(prefix, out var provider))
            return false;

        var result = await provider.LoadAsync(id);
        await AppendLoadedBatchesAsync(result);
        return result.Success;
    }

    public async Task<bool> AppendSourceByKindAsync(SongListQueueScopeKind kind, string id)
    {
        if (!_providersByKind.TryGetValue(kind, out var provider))
            return false;

        var result = await provider.LoadAsync(id);
        await AppendLoadedBatchesAsync(result);
        return result.Success;
    }

    public async Task<bool> AppendRadioListAsync(string radioId, bool asc = false)
    {
        if (_providersByKind.TryGetValue(SongListQueueScopeKind.Radio, out var provider)
            && provider is RadioQueueSourceProvider radioProvider)
        {
            var result = await radioProvider.LoadAsync(radioId, asc);
            await AppendLoadedBatchesAsync(result);
            return result.Success;
        }

        return false;
    }

    public async Task<bool> AppendSongsAsync(IEnumerable<SingleSongBase> songs, bool skipDuplicateSingle = false)
    {
        var songList = songs.ToList();
        if (songList.Count == 0)
            return false;

        if (skipDuplicateSingle && songList.Count == 1 && await ContainsProviderItemAsync(songList[0]).ConfigureAwait(false))
            return true;

        await _playCore.InsertSongRangeAsync(songList).ConfigureAwait(false);
        return true;
    }

    private async Task AppendLoadedBatchesAsync(NeteaseQueueSourceLoadResult result)
    {
        if (!result.Success)
            return;

        try
        {
            var songs = new List<SingleSongBase>();
            foreach (var batch in result.Batches)
            {
                if (batch is not { Count: > 0 })
                    continue;

                foreach (var providerSong in batch)
                {
                    if (result.Batches.Count == 1
                        && batch.Count == 1
                        && await ContainsProviderItemAsync(providerSong).ConfigureAwait(false))
                    {
                        continue;
                    }

                    songs.Add(providerSong);
                }
            }

            if (songs.Count > 0)
                await _playCore.InsertSongRangeAsync(songs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    private async Task<bool> ContainsProviderItemAsync(SingleSongBase song)
    {
        var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
        return queue.Any(providerItem =>
            providerItem.ProviderId == song.ProviderId
            && providerItem.TypeId == song.TypeId
            && providerItem.ActualId == song.ActualId);
    }
}
