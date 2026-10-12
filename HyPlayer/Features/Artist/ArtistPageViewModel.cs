using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Lists;
using ObservableCollections;

namespace HyPlayer.Features.Artist;

public partial class ArtistPageViewModel : ObservableObject
{
    private readonly ObservableList<ProvidableItemBase> _hotSongs = [];
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly IAppNavigator _navigator;
    private readonly INotificationService _notification;
    private readonly UISettings _uiSettings;
    private string _artistId = string.Empty;
    private Task<List<ContainerBase>>? _artistSubContainersTask;
    private Task<PersonBase?>? _artistTask;
    private string _loadedArtistId = string.Empty;
    private PersonBase? _providerArtist;

    public ArtistPageViewModel(
        IProvidableItemProvidable itemProvider,
        IProviderKnownTypeIds knownTypeIds,
        INotificationService notification,
        UISettings uiSettings,
        IAppNavigator navigator,
        ILoadEnvironment loadEnvironment)
    {
        ArtistLoad = new LoadState("歌手信息", loadEnvironment) { EmptyText = "没有找到这位歌手" };
        HotSongsLoad = new LoadState("热门歌曲", loadEnvironment) { EmptyText = "还没有热门歌曲" };
        _itemProvider = itemProvider;
        _knownTypeIds = knownTypeIds;
        _notification = notification;
        _uiSettings = uiSettings;
        _navigator = navigator;
    }

    [ObservableProperty] public partial PersonBase? Artist { get; set; }
    [ObservableProperty] public partial ContainerBase? AllSongsContainer { get; set; }
    [ObservableProperty] public partial ContainerBase? HotSongsContainer { get; set; }
    [ObservableProperty] public partial ContainerBase? AlbumsContainer { get; set; }
    [ObservableProperty] public partial int CurrentPivotIndex { get; set; }
    [ObservableProperty] public partial BitmapImage? Image { get; set; }
    [ObservableProperty] public partial bool IsPlayingAll { get; set; }
    public LoadState ArtistLoad { get; }
    public LoadState HotSongsLoad { get; }

    [RelayCommand]
    private async Task PlayAllSongsAsync()
    {
        if (IsPlayingAll || _providerArtist?.ActualId is not { Length: > 0 } artistId) return;

        IsPlayingAll = true;
        try
        {
            await _navigator.PlayAsync(new MusicResource.Artist(artistId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _notification.ShowFailure("播放全部失败", ex);
        }
        finally
        {
            IsPlayingAll = false;
        }
    }

    public Task InitializeArtistInfo(string artistId)
    {
        if (string.IsNullOrWhiteSpace(artistId))
        {
            _notification.ShowMessage("艺术家ID为空", "请检查传入的参数是否正确");
            return Task.CompletedTask;
        }

        if (_loadedArtistId == artistId && Artist is not null)
            return Task.CompletedTask;

        _artistId = artistId;
        return LoadArtist();
    }

    public void Cancel()
    {
        ArtistLoad.Cancel();
        HotSongsLoad.Cancel();
    }

    private Task LoadArtist()
    {
        if (ArtistLoad.IsLoading)
            return Task.CompletedTask;

        _artistTask = null;
        _artistSubContainersTask = null;
        var artist = ArtistLoad.RunAsync(GetArtistAsync, ApplyArtist, static artist => artist is null, LoadArtist);
        return Task.WhenAll(artist, RunHotSongsLoad());
    }

    private Task LoadHotSongs()
    {
        if (_artistTask is not { IsCompletedSuccessfully: true })
            return LoadArtist();

        return HotSongsLoad.IsLoading ? Task.CompletedTask : RunHotSongsLoad();
    }

    private Task RunHotSongsLoad()
    {
        return HotSongsLoad.RunAsync(LoadHotSongsAsync, ApplyHotSongs, static songs => songs.Count == 0,
            LoadHotSongs);
    }

    private Task<PersonBase?> GetArtistAsync(CancellationToken cancellationToken)
    {
        return (_artistTask ??= LoadProviderArtistAsync(_artistId)).WaitAsync(cancellationToken);
    }

    private async Task<PersonBase?> LoadProviderArtistAsync(string artistId)
    {
        return await SimpleCacher.GetOrCreateCacheOrThrowAsync(CacheType.ArtistDetail, artistId,
            async () => await GetProviderArtistAsync(artistId));
    }

    private void ApplyArtist(PersonBase? artist)
    {
        if (artist is null)
            return;

        _providerArtist = artist;
        Artist = artist;
        var image = (artist as NeteaseArtist)?.CoverUrl;
        Image = _uiSettings.NoImage || string.IsNullOrWhiteSpace(image) ? null : new BitmapImage(new Uri(image));
        _loadedArtistId = _artistId;

        AllSongsContainer = CreateArtistContainer(
            "tim", "全部歌曲", "artist-songs", _knownTypeIds.SingleSongTypeId,
            CacheType.ArtistSongsDetial);
        AlbumsContainer = CreateArtistContainer(
            "alb", "专辑", "artist-albums", _knownTypeIds.AlbumTypeId,
            CacheType.ArtistAlbumsList);
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
        var page = await SimpleCacher.GetOrCreateCacheOrThrowAsync(
            cacheType,
            $"{artistId}_{offset}_{count}",
            async () =>
            {
                var container = await GetArtistSubContainerAsync(_providerArtist, prefix);
                if (container is null)
                    return new ProgressivePage();

                var (hasMore, items) = await LoadRequestProbe.GuardAsync(
                    () => container.GetProgressiveItemsListAsync(offset, count, cancellationToken),
                    static loaded => loaded.Item2 is not { Count: > 0 });
                return new ProgressivePage
                {
                    HasMore = hasMore,
                    Items = items ?? []
                };
            });

        cancellationToken.ThrowIfCancellationRequested();
        return (page?.HasMore ?? false, page?.Items ?? []);
    }

    private async Task<List<ProvidableItemBase>> LoadHotSongsAsync(CancellationToken cancellationToken)
    {
        if (await GetArtistAsync(cancellationToken) is not { } artist)
            return [];

        var songs = await SimpleCacher.GetOrCreateCacheOrThrowAsync(CacheType.ArtistTopSongsDetail,
            artist.ActualId,
            async () =>
            {
                var container = await GetArtistSubContainerAsync(artist, "hot");
                return container is null
                    ? []
                    : (await LoadRequestProbe.GuardAsync(
                        () => container.GetProgressiveItemsListAsync(0, 50, cancellationToken),
                        static loaded => loaded.Item2 is not { Count: > 0 })).Item2;
            },
            cancellationToken: cancellationToken);
        return songs ?? [];
    }

    private void ApplyHotSongs(List<ProvidableItemBase> songs)
    {
        _hotSongs.Clear();
        _hotSongs.AddRange(songs);
        HotSongsContainer = new StaticItemsContainer(_hotSongs, "热门歌曲", "artist-hot");
    }

    private async Task<PersonBase> GetProviderArtistAsync(string artistId)
    {
        try
        {
            if (await LoadRequestProbe.GuardAsync(
                    () => _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.ArtistTypeId + artistId),
                    static item => item is not PersonBase)
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

    private async Task<IProgressiveLoadingContainer?> GetArtistSubContainerAsync(PersonBase? artist, string prefix)
    {
        if (_artistSubContainersTask is { IsFaulted: true } or { IsCanceled: true })
            _artistSubContainersTask = null;

        var subContainers = artist is null
            ? []
            : await (_artistSubContainersTask ??= artist.GetSubContainerAsync());
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
