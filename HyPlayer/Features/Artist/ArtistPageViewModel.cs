using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Platform.Storage.Cache;
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
    public partial class ArtistPageViewModel : ObservableObject
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

        private readonly ObservableCollection<ProvidableItemBase> _allSongs = [];
        private readonly ObservableCollection<ProvidableItemBase> _hotSongs = [];
        private readonly ObservableCollection<ProvidableItemBase> _albums = [];
        [ObservableProperty]
        public partial PersonBase Artist { get; set; }
        [ObservableProperty]
        public partial ContainerBase AllSongsContainer { get; set; }
        [ObservableProperty]
        public partial ContainerBase HotSongsContainer { get; set; }
        [ObservableProperty]
        public partial ContainerBase AlbumsContainer { get; set; }
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

            _hotSongs.Clear();
            var songs = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, Artist.ActualId, async () =>
            {
                var container = await GetArtistSubContainerAsync("hot");
                return container is null ? [] : await LoadProgressiveItemsAsync(container, 0, 50);
            });
            if (songs is null)
            {
                return;
            }

            foreach (var item in songs)
                _hotSongs.Add(item);
            HotSongsContainer = new StaticItemsContainer(_hotSongs, "热门歌曲", "artist-hot");
        }

        private async Task LoadSongs()
        {
            if (CurrentPage == 0) _allSongs.Clear();
            var page = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistSongsDetial, Artist.ActualId + "_" + CurrentPage,
                    async () =>
                    {
                        var container = await GetArtistSubContainerAsync("tim");
                        return container is null
                            ? new ProgressivePage<ProvidableItemBase>()
                            : await LoadProgressivePageAsync<ProvidableItemBase>(container, CurrentPage * 50, 50);
                    });
            foreach (var item in page?.Items ?? [])
            {
                _allSongs.Add(item);
            }
            AllSongsContainer = new StaticItemsContainer(_allSongs, "全部歌曲", "artist-songs");
            HasNextPage = page?.HasMore ?? false;
            HasPreviousPage = CurrentPage > 0;
        }
        private async Task LoadAlbum()
        {
            try
            {
                _albums.Clear();
                var page = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistAlbumsList, Artist.ActualId + "_" + CurrentPage,
                    async () =>
                    {
                        var container = await GetArtistSubContainerAsync("alb");
                        return container is null
                            ? new ProgressivePage<AlbumBase>()
                            : await LoadProgressivePageAsync<AlbumBase>(container, CurrentPage * 50, 50);
                    });

                foreach (var album in page?.Items ?? [])
                {
                    _albums.Add(album);
                }
                AlbumsContainer = new StaticItemsContainer(_albums, "专辑", "artist-albums");
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
                _allSongs.Clear();
            else if (CurrentPivotIndex == 2)
                _albums.Clear();
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
                _allSongs.Clear();
            else if (CurrentPivotIndex == 2)
                _albums.Clear();
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
