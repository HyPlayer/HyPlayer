using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
///     专辑源提供者 — 加载 provider 专辑全部歌曲。
///     Prefix: "al", Kind: <see cref="SongListQueueScopeKind.Album" />
/// </summary>
internal sealed class AlbumQueueSourceProvider : IQueueSourceProvider
{
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;
    private readonly IProvidableItemProvidable _provider;

    public AlbumQueueSourceProvider(IProvidableItemProvidable provider, IProviderKnownTypeIds knownTypeIds,
        INotificationService notification)
    {
        _provider = provider;
        _knownTypeIds = knownTypeIds;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Album;
    public string Prefix => QueueSourcePrefixes.Album;
    public bool SupportCompleteLoad => true;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var album = await _provider.GetProvidableItemByIdAsync(_knownTypeIds.AlbumTypeId + id, cancellationToken);
            var songs = album switch
            {
                LinerContainerBase linerAlbum => await linerAlbum.GetAllItemsAsync(cancellationToken),
                IProgressiveLoadingContainer progressiveAlbum => await LoadAllProgressiveItemsAsync(progressiveAlbum,
                    cancellationToken),
                _ => null
            };

            return songs is null
                ? ProviderQueueSourceLoadResult.Failed
                : ProviderQueueSourceLoadResult.FromSongs(songs.OfType<SingleSongBase>().ToList());
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendAlbum时发生错误", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }

    private static async Task<List<ProvidableItemBase>> LoadAllProgressiveItemsAsync(
        IProgressiveLoadingContainer container,
        CancellationToken cancellationToken)
    {
        var items = new List<ProvidableItemBase>();
        var offset = 0;
        var count = container.MaxProgressiveCount;
        var hasMore = true;

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await container.GetProgressiveItemsListAsync(offset, count, cancellationToken);
            hasMore = result.Item1;
            if (result.Item2.Count == 0)
                break;

            items.AddRange(result.Item2);
            offset += result.Item2.Count;
        }

        return items;
    }
}