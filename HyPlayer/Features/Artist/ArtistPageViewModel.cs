using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.UI.Lists;
using ObservableCollections;

namespace HyPlayer.Features.Artist;

public partial class ArtistPageViewModel : ObservableObject
{
    private readonly ObservableList<ProvidableItemBase> _hotSongs = [];
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;
    private readonly UISettings _uiSettings;
    private Task<List<ContainerBase>>? _artistSubContainersTask;
    private string _loadedArtistId = string.Empty;
    private PersonBase? _providerArtist;

    public ArtistPageViewModel(
        IProvidableItemProvidable itemProvider,
        IProviderKnownTypeIds knownTypeIds,
        INotificationService notification,
        UISettings uiSettings)
    {
        _itemProvider = itemProvider;
        _knownTypeIds = knownTypeIds;
        _notification = notification;
        _uiSettings = uiSettings;
    }

    [ObservableProperty] public partial PersonBase? Artist { get; set; }
    [ObservableProperty] public partial ContainerBase? AllSongsContainer { get; set; }
    [ObservableProperty] public partial ContainerBase? HotSongsContainer { get; set; }
    [ObservableProperty] public partial ContainerBase? AlbumsContainer { get; set; }
    [ObservableProperty] public partial int CurrentPivotIndex { get; set; }
    [ObservableProperty] public partial BitmapImage? Image { get; set; }

    public async Task InitializeArtistInfo(string artistId)
    {
        if (string.IsNullOrWhiteSpace(artistId))
        {
            _notification.ShowMessage("艺术家ID为空", "请检查传入的参数是否正确");
            return;
        }

        if (_loadedArtistId == artistId && Artist is not null)
            return;

        _providerArtist = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistDetail, artistId, async () =>
        {
            try
            {
                return await GetProviderArtistAsync(artistId);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or TaskCanceledException))
            {
                _notification.ShowMessage("获取艺术家信息失败", ex.Message);
                return null;
            }
        });

        if (_providerArtist is null)
            return;

        Artist = _providerArtist;
        var image = (_providerArtist as NeteaseArtist)?.CoverUrl;
        Image = _uiSettings.NoImage || string.IsNullOrWhiteSpace(image) ? null : new BitmapImage(new Uri(image));
        _loadedArtistId = artistId;
        _artistSubContainersTask = null;

        AllSongsContainer = CreateArtistContainer(
            "tim", "全部歌曲", "artist-songs", _knownTypeIds.SingleSongTypeId,
            CacheType.ArtistSongsDetial);
        AlbumsContainer = CreateArtistContainer(
            "alb", "专辑", "artist-albums", _knownTypeIds.AlbumTypeId,
            CacheType.ArtistAlbumsList);
        LoadHotSongs().SafeFireAndForget();
    }

    private ContainerBase CreateArtistContainer(
        string prefix,
        string name,
        string actualIdSuffix,
        string typeId,
        CacheType cacheType)
    {
        var artistId = _providerArtist?.ActualId ?? string.Empty;
        return new DelegateProgressiveContainer(
            (offset, count, cancellationToken) =>
                LoadArtistPageAsync(prefix, cacheType, offset, count, cancellationToken),
            name,
            $"{artistId}:{actualIdSuffix}",
            typeId,
            _providerArtist?.ProviderId ?? string.Empty,
            50);
    }

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> LoadArtistPageAsync(
        string prefix,
        CacheType cacheType,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var artistId = _providerArtist?.ActualId ?? string.Empty;
        var page = await SimpleCacher.GetOrCreateCacheAsync(
            cacheType,
            $"{artistId}_{offset}_{count}",
            async () =>
            {
                var container = await GetArtistSubContainerAsync(prefix);
                if (container is null)
                    return new ProgressivePage();

                var (hasMore, items) = await container.GetProgressiveItemsListAsync(
                    offset, count, cancellationToken);
                return new ProgressivePage
                {
                    HasMore = hasMore,
                    Items = items ?? []
                };
            });

        cancellationToken.ThrowIfCancellationRequested();
        return (page?.HasMore ?? false, page?.Items ?? []);
    }

    private async Task LoadHotSongs()
    {
        if (Artist is null)
            return;

        _hotSongs.Clear();
        var songs = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, Artist.ActualId,
            async () =>
            {
                var container = await GetArtistSubContainerAsync("hot");
                return container is null
                    ? []
                    : (await container.GetProgressiveItemsListAsync(0, 50)).Item2;
            });
        if (songs is null)
            return;

        _hotSongs.AddRange(songs);
        HotSongsContainer = new StaticItemsContainer(_hotSongs, "热门歌曲", "artist-hot");
    }

    private async Task<PersonBase> GetProviderArtistAsync(string artistId)
    {
        try
        {
            if (await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.ArtistTypeId + artistId)
                is PersonBase artist)
                return artist;
        }
        catch (NotImplementedException)
        {
            // Current provider builds artist subcontainers from ActualId; fall back until artist lookup is implemented.
        }

        return new LocalArtist
        {
            ActualId = artistId,
            Name = artistId
        };
    }

    private async Task<IProgressiveLoadingContainer?> GetArtistSubContainerAsync(string prefix)
    {
        var subContainers = _providerArtist is null
            ? []
            : await (_artistSubContainersTask ??= _providerArtist.GetSubContainerAsync());
        return subContainers.OfType<IProgressiveLoadingContainer>()
            .FirstOrDefault(container => (container as ProvidableItemBase)?.ActualId?.StartsWith(prefix) is true);
    }

    private sealed class LocalArtist : PersonBase
    {
        public override string ProviderId => string.Empty;
        public override string TypeId => string.Empty;

        public override Task<List<ContainerBase>> GetSubContainerAsync(CancellationToken ctk = default)
        {
            return Task.FromResult(new List<ContainerBase>());
        }
    }

    private sealed class ProgressivePage
    {
        public bool HasMore { get; init; }
        public List<ProvidableItemBase> Items { get; init; } = [];
    }
}
