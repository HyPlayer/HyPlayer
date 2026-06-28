using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.QueueProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    public async Task<ProviderQueueSourceLoadResult> LoadSourceByKindAsync(
        SongListQueueScopeKind kind,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!_providersByKind.TryGetValue(kind, out var provider))
            return ProviderQueueSourceLoadResult.Failed;

        cancellationToken.ThrowIfCancellationRequested();
        return await provider.LoadAsync(id, cancellationToken);
    }

    public async Task<bool> AppendNcSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (sourceId.Length < 3)
            return false;

        cancellationToken.ThrowIfCancellationRequested();
        var prefix = sourceId[..2];
        var id = sourceId[2..];

        if (!_providersByPrefix.TryGetValue(prefix, out var provider))
            return false;

        var result = await provider.LoadAsync(id, cancellationToken);
        await AppendLoadedBatchesAsync(result, cancellationToken);
        return result.Success;
    }

    public async Task<bool> AppendSourceByKindAsync(
        SongListQueueScopeKind kind,
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await LoadSourceByKindAsync(kind, id, cancellationToken);
        await AppendLoadedBatchesAsync(result, cancellationToken);
        return result.Success;
    }

    public async Task<bool> AppendRadioListAsync(
        string radioId,
        bool asc = false,
        CancellationToken cancellationToken = default)
    {
        if (_providersByKind.TryGetValue(SongListQueueScopeKind.Radio, out var provider)
            && provider is RadioQueueSourceProvider radioProvider)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await radioProvider.LoadAsync(radioId, asc, cancellationToken);
            await AppendLoadedBatchesAsync(result, cancellationToken);
            return result.Success;
        }

        return false;
    }

    public async Task<bool> AppendSongsAsync(
        IEnumerable<SingleSongBase> songs,
        bool skipDuplicateSingle = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var songList = songs.ToList();
        if (songList.Count == 0)
            return false;

        if (skipDuplicateSingle && songList.Count == 1 && await ContainsProviderItemAsync(songList[0], cancellationToken).ConfigureAwait(false))
            return true;

        await _playCore.InsertSongRangeAsync(songList, ctk: cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task AppendLoadedBatchesAsync(
        ProviderQueueSourceLoadResult result,
        CancellationToken cancellationToken = default)
    {
        if (!result.Success)
            return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var songs = new List<SingleSongBase>();
            var skipDuplicateSingle = result.Batches is [{ Count: 1 }];
            var existingQueue = skipDuplicateSingle
                ? await _playCore.GetPlaylistAsync(cancellationToken).ConfigureAwait(false)
                : null;
            foreach (var batch in result.Batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (batch is not { Count: > 0 })
                    continue;

                foreach (var providerSong in batch)
                {
                    if (existingQueue is not null && ContainsProviderItem(existingQueue, providerSong))
                    {
                        continue;
                    }

                    songs.Add(providerSong);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (songs.Count > 0)
                await _playCore.InsertSongRangeAsync(songs, ctk: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    private async Task<bool> ContainsProviderItemAsync(
        SingleSongBase song,
        CancellationToken cancellationToken = default)
    {
        var queue = await _playCore.GetPlaylistAsync(cancellationToken).ConfigureAwait(false);
        return ContainsProviderItem(queue, song);
    }

    private static bool ContainsProviderItem(IReadOnlyList<SingleSongBase> queue, SingleSongBase song)
    {
        return queue.Any(providerItem =>
            providerItem.ProviderId == song.ProviderId
            && providerItem.TypeId == song.TypeId
            && providerItem.ActualId == song.ActualId);
    }
}
