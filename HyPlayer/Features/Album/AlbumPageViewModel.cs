using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Downloads;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Features.Album
{
    public partial class AlbumPageViewModel : ObservableRecipient
    {
        private readonly IPlaylistService _playlist;
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private readonly INavigationService _navigation;
        private readonly IAppNavigator _navigator;
        private readonly IBackgroundTaskRunner _taskRunner;

        public AlbumPageViewModel(
            IPlaylistService playlist,
            global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
            Setting setting,
            INotificationService notification,
            INavigationService navigation,
            IAppNavigator navigator,
            IBackgroundTaskRunner taskRunner)
        {
            _playlist = playlist;
            _neteaseProvider = neteaseProvider;
            _setting = setting;
            _notification = notification;
            _navigation = navigation;
            _navigator = navigator;
            _taskRunner = taskRunner;
            QueueScope = SongListQueueScope.Visible;
        }

        [ObservableProperty]
        public partial NCAlbum Album { get; set; }
        [ObservableProperty]
        public partial CollectionViewSource AlbumSongsViewSource { get; set; }
        private List<SingleSongBase> _providerAlbumSongs = [];
        [ObservableProperty]
        public partial List<NCArtist> Artists { get; set; }
        [ObservableProperty]
        public partial string AuthorString { get; set; }
        [ObservableProperty]
        public partial string Description { get; set; }
        [ObservableProperty]
        public partial SongListQueueScope QueueScope { get; set; }
        [ObservableProperty]
        public partial bool Subscribed { get; set; }
        [ObservableProperty]
        public partial BitmapImage SourceImage { get; set; }
        [ObservableProperty]
        public partial long PublishTime { get; set; }

        public async Task LoadAlbumDynamic(string albumId)
        {
            var album = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumDynamic, albumId, async () =>
            {
                return await LoadProviderAlbumAsync(albumId);
            });

            if (album is not null)
            {
                Subscribed = album.IsSubscribed;
            }
        }

        public async Task LoadAlbumInfo(string albumId)
        {
            try
            {
                var providerAlbum = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumInfo, albumId, async () =>
                    await LoadProviderAlbumAsync(albumId));

                if (providerAlbum is null)
                {
                    return;
                }

                Album = MapToNcAlbum(providerAlbum);
                if (!_setting.noImage) SourceImage = new BitmapImage(new Uri(Album.Cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER));
                else SourceImage = new BitmapImage(new Uri("/Assets/icon.png"));

                var artists = providerAlbum.Artists?.Select(artist => MapToNcArtist(artist)).ToList();
                AuthorString = string.Join(" / ", artists?.Select(t => t.Name) ?? []);
                Description = (providerAlbum.Alias is { Count: > 0 } ? string.Join(" / ", providerAlbum.Alias) + "\r\n" : string.Empty) + providerAlbum.Description;
                var idx = 0;
                QueueScope = SongListQueueScope.Album(Album.Id);
                PublishTime = 0;
                var songs = await LoadAlbumSongsAsync(providerAlbum);
                _providerAlbumSongs = songs;
                AlbumSongsViewSource = new CollectionViewSource()
                {
                    IsSourceGrouped = true,
                    Source = songs.Select(song => MapToNcAlbumSong(song, ++idx))
                    .GroupBy(t => t.DiscName).OrderBy(t => t.Key)
                    .Select(t => new SongListItemGroup(t.Select(SongListItemViewModel.FromNCSong)) { Key = t.Key }).ToList()
                };
            }
            catch (Exception ex)
            {
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        [RelayCommand]
        public async Task PlayAll()
        {
            try
            {
                _playlist.Clear();
                await _navigator.AppendAsync(new MusicResource.Album(Album.Id));
                await _playlist.MoveToIndexAsync(0);
            }
            catch (Exception ex)
            {
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        [RelayCommand]
        private void DownloadAll()
        {
            if (_providerAlbumSongs.Count > 0)
                DownloadManager.AddDownload(_providerAlbumSongs);
            else
            {
                var songs = new List<SingleSongBase>();
                foreach (var discSongs in (IEnumerable<SongListItemGroup>)AlbumSongsViewSource.Source)
                    songs.AddRange(discSongs.Select(song => song.ToProviderSong()));
                DownloadManager.AddDownload(songs);
            }
        }

        [RelayCommand]
        private void NavigateComment()
        {
            _navigation.Navigate(typeof(Comments.Comments), CommentTarget.Album(Album.Id));
        }

        [RelayCommand]
        private void Subscribe()
        {
            _taskRunner.Forget(Subscribed
                    ? new NeteaseAlbum { ActualId = Album.Id, Name = Album.Name }.UnsubscribeAsync()
                    : new NeteaseAlbum { ActualId = Album.Id, Name = Album.Name }.SubscribeAsync(),
                "toggle album subscription");
            Subscribed = !Subscribed;
        }

        [RelayCommand]
        private void AddAllToPlaylist()
        {
            _navigator.AppendAsync(new MusicResource.Album(Album.Id)).SafeFireAndForget();
        }

        private async Task<NeteaseAlbum> LoadProviderAlbumAsync(string albumId)
        {
            if (await _neteaseProvider.GetAlbumById(albumId) is { } album)
            {
                return album;
            }

            var item = await _neteaseProvider.GetProvidableItemByIdAsync(NeteaseTypeIds.Album + albumId);
            if (item is NeteaseAlbum providerAlbum)
            {
                return providerAlbum;
            }

            _notification.ShowMessage("获取专辑信息失败", "未能从网易云提供程序加载专辑");
            return null;
        }

        private static async Task<List<SingleSongBase>> LoadAlbumSongsAsync(NeteaseAlbum album)
        {
            List<ProvidableItemBase> items = (await album.GetProgressiveItemsListAsync(0, album.MaxProgressiveCount)).Item2;
            if (items is null or { Count: 0 })
            {
                items = await album.GetAllItemsAsync();
            }

            return items.OfType<SingleSongBase>().ToList();
        }

        private static NCAlbum MapToNcAlbum(NeteaseAlbum album)
        {
            return new NCAlbum
            {
                AlbumType = HyPlayItemType.Netease,
                Alias = album.Translation,
                Cover = album.PictureUrl,
                Description = album.Description,
                Id = album.ActualId,
                Name = album.Name
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

        private static NCArtist MapToNcArtist(PersonBase artist)
        {
            return new NCArtist
            {
                Id = artist.ActualId,
                Name = artist.Name,
                Type = HyPlayItemType.Netease
            };
        }

        private static NCAlbumSong MapToNcAlbumSong(SingleSongBase song, int order)
        {
            var neteaseSong = song as NeteaseSong;
            return new NCAlbumSong
            {
                Album = song.Album is NeteaseAlbum neteaseAlbum ? MapToNcAlbum(neteaseAlbum) : new NCAlbum
                {
                    AlbumType = HyPlayItemType.Netease,
                    Cover = neteaseSong?.CoverUrl,
                    Id = song.Album?.ActualId,
                    Name = song.Album?.Name
                },
                Alias = neteaseSong?.Alias is not null ? string.Join(",", neteaseSong.Alias) : null,
                Artist = GetArtists(song),
                DiscName = neteaseSong?.CdName,
                CDName = neteaseSong?.CdName,
                IsCloud = false,
                IsVip = false,
                LengthInMilliseconds = song.Duration,
                MVId = neteaseSong?.MvId,
                SongId = song.ActualId,
                Order = order,
                SongName = song.Name,
                TrackId = neteaseSong?.TrackNumber ?? 0,
                TranslatedName = neteaseSong?.Translation,
                IsAvailable = song.Available,
                Type = HyPlayItemType.Netease,
            };
        }

        private static List<NCArtist> GetArtists(SingleSongBase song)
        {
            if (song is NeteaseSong { Artists: { Count: > 0 } artists })
            {
                return artists.Select(artist => MapToNcArtist(artist)).ToList();
            }

            return song.CreatorList?.Select(name => new NCArtist
            {
                Name = name,
                Type = HyPlayItemType.Netease
            }).ToList() ?? [];
        }
    }
}
