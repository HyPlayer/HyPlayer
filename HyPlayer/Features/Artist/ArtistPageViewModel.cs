using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Features.Artist
{
    public partial class ArtistPageViewModel : ObservableRecipient
    {
        private readonly IProvidableItemProvidable _itemProvider;
        private readonly IProviderKnownTypeIds _knownTypeIds;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private PersonBase _providerArtist;
        private Task<List<ContainerBase>> _artistSubContainersTask;
        private string _loadedArtistId = string.Empty;

        public ArtistPageViewModel(
            IProvidableItemProvidable itemProvider,
            IProviderKnownTypeIds knownTypeIds,
            Setting setting,
            INotificationService notification)
        {
            _itemProvider = itemProvider;
            _knownTypeIds = knownTypeIds;
            _setting = setting;
            _notification = notification;
        }

        public ObservableCollection<SongListItemViewModel> AllSongs { get; set; } = [];
        public ObservableCollection<SongListItemViewModel> HotSongs { get; set; } = [];
        public ObservableCollection<SimpleListItem> Albums { get; set; } = [];
        [ObservableProperty]
        public partial PersonBase Artist { get; set; }
        [ObservableProperty]
        public partial int CurrentPage { get; set; } = 0;
        [ObservableProperty]
        public partial int CurrentPivotIndex { get; set; } = 0;
        [ObservableProperty]
        public partial bool HasNextPage { get; set; }
        [ObservableProperty]
        public partial bool HasPreviousPage { get; set; }
        [ObservableProperty]
        public partial BitmapImage Image { get; set; }

        public async Task InitializeArtistInfo(string artistId)
        {
            if (artistId is null)
            {
                _notification.ShowMessage("艺人ID为空", "请检查传入的参数是否正确");
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
                catch (Exception ex) when (!(ex is OperationCanceledException or TaskCanceledException))
                {
                    _notification.ShowMessage("获取艺人信息失败", ex.Message);
                    return null;
                }
            });

            if (_providerArtist is null)
            {
                return;
            }

            Artist = _providerArtist;
            _loadedArtistId = artistId;
            _artistSubContainersTask = null;
            LoadHotSongs().SafeFireAndForget();
            LoadSongs().SafeFireAndForget();
            LoadAlbum().SafeFireAndForget();
        }
        private async Task LoadHotSongs()
        {

            HotSongs.Clear();
            var songs = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, Artist.ActualId, async () =>
            {
                var container = await GetArtistSubContainerAsync("hot");
                return container is null ? [] : await LoadProgressiveItemsAsync(container, 0, 50);
            });
            var idx = 0;
            if (songs is null)
            {
                return;
            }

            foreach (var item in songs.OfType<SingleSongBase>())
            {
                HotSongs.Add(await SongListItemViewModel.FromProviderSongAsync(item, idx++));
            }
        }

        private async Task LoadSongs()
        {
            if (CurrentPage == 0) AllSongs.Clear();
            var page = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistSongsDetial, Artist.ActualId + "_" + CurrentPage,
                    async () =>
                    {
                        var container = await GetArtistSubContainerAsync("tim");
                        return container is null
                            ? new ProgressivePage<SingleSongBase>()
                            : await LoadProgressivePageAsync<SingleSongBase>(container, CurrentPage * 50, 50);
                    });
            var idx = 0;
            foreach (var item in page?.Items ?? [])
            {
                AllSongs.Add(await SongListItemViewModel.FromProviderSongAsync(item, CurrentPage * 50 + idx++));
            }
            HasNextPage = page?.HasMore ?? false;
            HasPreviousPage = CurrentPage > 0;
        }
        private async Task LoadAlbum()
        {
            try
            {
                Albums.Clear();
                var page = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistAlbumsList, Artist.ActualId + "_" + CurrentPage,
                    async () =>
                    {
                        var container = await GetArtistSubContainerAsync("alb");
                        return container is null
                            ? new ProgressivePage<AlbumBase>()
                            : await LoadProgressivePageAsync<AlbumBase>(container, CurrentPage * 50, 50);
                    });

                var i = 0;
                foreach (var album in page?.Items ?? [])
                {
                    Albums.Add(await MapToSimpleListItemAsync(album, CurrentPage * 50 + i++));
                }
                HasNextPage = page?.HasMore ?? false;
                HasPreviousPage = CurrentPage > 0;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException or TaskCanceledException))
            {
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        [RelayCommand]
        private void NextPage()
        {
            CurrentPage++;
            if (CurrentPivotIndex == 1)
                AllSongs.Clear();
            else if (CurrentPivotIndex == 2)
                Albums.Clear();
            if (CurrentPivotIndex == 1)
                LoadSongs().SafeFireAndForget();
            else if (CurrentPivotIndex == 2)
                LoadAlbum().SafeFireAndForget();
        }
        [RelayCommand]
        private void PreviousPage()
        {
            CurrentPage--;
            if (CurrentPivotIndex == 1)
                AllSongs.Clear();
            else if (CurrentPivotIndex == 2)
                Albums.Clear();
            if (CurrentPivotIndex == 1)
                LoadSongs().SafeFireAndForget();
            else if (CurrentPivotIndex == 2)
                LoadAlbum().SafeFireAndForget();
        }

        private async Task<PersonBase> GetProviderArtistAsync(string artistId)
        {
            try
            {
                if (await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.ArtistTypeId + artistId) is PersonBase artist)
                {
                    return artist;
                }
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

        private static async Task<List<ProvidableItemBase>> LoadProgressiveItemsAsync(IProgressiveLoadingContainer container, int start, int count)
        {
            return (await container.GetProgressiveItemsListAsync(start, count)).Item2;
        }

        private static async Task<ProgressivePage<T>> LoadProgressivePageAsync<T>(IProgressiveLoadingContainer container, int start, int count)
            where T : ProvidableItemBase
        {
            var (hasMore, items) = await container.GetProgressiveItemsListAsync(start, count);
            return new ProgressivePage<T>
            {
                HasMore = hasMore,
                Items = items.OfType<T>().ToList()
            };
        }

        private static async Task<SimpleListItem> MapToSimpleListItemAsync(AlbumBase album, int order)
        {
            var creators = album is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync() : null;
            var aliases = album is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
            var cover = album is IHasCover coverProvider ? await coverProvider.GetCoverAsync() : null;
            var coverUri = cover is IResourceResultOf<Uri?> uriResult ? await uriResult.GetResourceAsync() : null;
            return new SimpleListItem
            {
                Title = album.Name,
                LineOne = string.Join("/", creators?.Select(t => t.Name) ?? []),
                LineTwo = aliases != null
                    ? string.Join(" / ", aliases)
                    : "",
                LineThree = "",
                Route = new AppRoute.Album($"{album.ActualId}"),
                PlayResource = new MusicResource.Album($"{album.ActualId}"),
                CoverLink = coverUri?.ToString(),
                Order = order,
                CanPlay = true
            };
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

        private sealed class ProgressivePage<T> where T : ProvidableItemBase
        {
            public bool HasMore { get; set; }
            public List<T> Items { get; set; } = [];
        }
    }
}
