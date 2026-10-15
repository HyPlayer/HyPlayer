using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.QueueProviders;

internal sealed class ArtistSongsQueueSourceProvider : IQueueSourceProvider
{
    private const string AllSongsContainerPrefix = "tim";
    private const int PageSize = 100;
    private const int MaxSongCount = 1000;

    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;
    private readonly IProvidableItemProvidable _provider;

    public ArtistSongsQueueSourceProvider(IProvidableItemProvidable provider, IProviderKnownTypeIds knownTypeIds,
        INotificationService notification)
    {
        _provider = provider;
        _knownTypeIds = knownTypeIds;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Artist;
    public string Prefix { get; } = new MusicResource.Artist(string.Empty).ToPlaybackSourceKey();
    public bool SupportCompleteLoad => true;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _provider.GetProvidableItemByIdAsync(_knownTypeIds.ArtistTypeId + id, cancellationToken)
                is not PersonBase artist)
                return ProviderQueueSourceLoadResult.Failed;

            var container = (await artist.GetSubContainerAsync(cancellationToken))
                .OfType<IProgressiveLoadingContainer>()
                .FirstOrDefault(candidate =>
                    (candidate as ProvidableItemBase)?.ActualId?.StartsWith(AllSongsContainerPrefix) is true);
            if (container is null)
                return ProviderQueueSourceLoadResult.Failed;

            var songs = new List<SingleSongBase>();
            for (var offset = 0; offset < MaxSongCount; offset += PageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (hasMore, items) = await container.GetProgressiveItemsListAsync(offset, PageSize,
                    cancellationToken);
                songs.AddRange(items.OfType<SingleSongBase>().Where(song => song.Available));
                if (!hasMore || items.Count == 0)
                    break;
            }

            if (songs.Count > 0)
                return ProviderQueueSourceLoadResult.FromSongs(songs);

            _notification.ShowMessage("没有可播放的歌曲", artist.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _notification.ShowFailure("获取歌手歌曲失败", ex);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}
