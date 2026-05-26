using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.NeteaseProvider.Models;
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
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Features.Artist
{
    public partial class ArtistPageViewModel : ObservableRecipient
    {
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private NeteaseArtist _providerArtist;

        public ArtistPageViewModel(
            global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
            Setting setting,
            INotificationService notification)
        {
            _neteaseProvider = neteaseProvider;
            _setting = setting;
            _notification = notification;
        }

        public ObservableCollection<SongListItemViewModel> AllSongs { get; set; } = [];
        public ObservableCollection<SongListItemViewModel> HotSongs { get; set; } = [];
        public ObservableCollection<SimpleListItem> Albums { get; set; } = [];
        [ObservableProperty]
        public partial NCArtist Artist { get; set; }
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

            Artist = MapToNcArtist(_providerArtist);
            if (Artist.Avatar?.StartsWith("http") is true)
            {
                if (_setting.noImage)
                {
                    Image = null;
                }

                BitmapImage image = new(new Uri(Artist.Avatar + "?param=" + StaticSource.PICSIZE_ARTIST_DETAIL_COVER));
                Image = image;
            }
            LoadHotSongs().SafeFireAndForget();
            LoadSongs().SafeFireAndForget();
            LoadAlbum().SafeFireAndForget();
        }
        private async Task LoadHotSongs()
        {

            HotSongs.Clear();
            var songs = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, Artist.Id, async () =>
            {
                var container = await GetArtistSubContainerAsync("hot");
                return await LoadProgressiveItemsAsync(container, 0, 50);
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
            var page = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistSongsDetial, Artist.Id + "_" + CurrentPage,
                    async () =>
                    {
                        var container = await GetArtistSubContainerAsync("tim");
                        return await LoadProgressivePageAsync<SingleSongBase>(container, CurrentPage * 50, 50);
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
                var page = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistAlbumsList, Artist.Id + "_" + CurrentPage,
                    async () =>
                    {
                        var container = await GetArtistSubContainerAsync("alb");
                        return await LoadProgressivePageAsync<NeteaseAlbum>(container, CurrentPage * 50, 50);
                    });

                var i = 0;
                foreach (var album in page?.Items ?? [])
                {
                    Albums.Add(MapToSimpleListItem(album, CurrentPage * 50 + i++));
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
                LoadSongs().SafeFireAndForget();
            else if (CurrentPivotIndex == 2)
                LoadAlbum().SafeFireAndForget();
        }
        [RelayCommand]
        private void PreviousPage()
        {
            CurrentPage--;
            if (CurrentPivotIndex == 1)
                LoadSongs().SafeFireAndForget();
            else if (CurrentPivotIndex == 2)
                LoadAlbum().SafeFireAndForget();
        }

        private async Task<NeteaseArtist> GetProviderArtistAsync(string artistId)
        {
            try
            {
                if (await _neteaseProvider.GetProvidableItemByIdAsync(NeteaseTypeIds.Artist + artistId) is NeteaseArtist artist)
                {
                    return artist;
                }
            }
            catch (NotImplementedException)
            {
                // Current provider builds artist subcontainers from ActualId; fall back until artist lookup is implemented.
            }

            return new NeteaseArtist
            {
                ActualId = artistId,
                Name = artistId
            };
        }

        private async Task<IProgressiveLoadingContainer> GetArtistSubContainerAsync(string prefix)
        {
            var subContainers = _providerArtist is null ? [] : await _providerArtist.GetSubContainerAsync();
            return subContainers.OfType<NeteaseArtistSubContainer>()
                       .FirstOrDefault(container => container.ActualId?.StartsWith(prefix) is true)
                   ?? new NeteaseArtistSubContainer
                   {
                       ActualId = prefix + Artist.Id,
                       Name = Artist.Name
                   };
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

        private static NCArtist MapToNcArtist(NeteaseArtist artist)
        {
            return new NCArtist
            {
                Id = artist.ActualId,
                Name = artist.Name,
                Type = HyPlayItemType.Netease
            };
        }

        private static SimpleListItem MapToSimpleListItem(NeteaseAlbum album, int order)
        {
            return new SimpleListItem
            {
                Title = album.Name,
                LineOne = string.Join("/", album.CreatorList ?? album.Artists?.Select(t => t.Name) ?? []),
                LineTwo = album.Alias != null
                    ? string.Join(" / ", album.Alias)
                    : "",
                LineThree = album.AlbumType ?? "",
                Route = new AppRoute.Album($"{album.ActualId}"),
                PlayResource = new MusicResource.Album($"{album.ActualId}"),
                CoverLink = album.PictureUrl,
                Order = order,
                CanPlay = true
            };
        }

        private sealed class ProgressivePage<T> where T : ProvidableItemBase
        {
            public bool HasMore { get; set; }
            public List<T> Items { get; set; } = [];
        }
    }
}
