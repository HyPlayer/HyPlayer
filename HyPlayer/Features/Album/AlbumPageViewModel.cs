using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction;
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
        private readonly PlayCoreBase _playCore;
        private readonly IPlaybackControlService _control;
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private readonly INavigationService _navigation;
        private readonly IAppNavigator _navigator;
        private readonly IBackgroundTaskRunner _taskRunner;
        private string _providerAlbumTaskId;
        private Task<NeteaseAlbum> _providerAlbumTask;

        public AlbumPageViewModel(
            PlayCoreBase playCore,
            IPlaybackControlService control,
            global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
            Setting setting,
            INotificationService notification,
            INavigationService navigation,
            IAppNavigator navigator,
            IBackgroundTaskRunner taskRunner)
        {
            _playCore = playCore;
            _control = control;
            _neteaseProvider = neteaseProvider;
            _setting = setting;
            _notification = notification;
            _navigation = navigation;
            _navigator = navigator;
            _taskRunner = taskRunner;
            QueueScope = SongListQueueScope.Visible;
        }

        [ObservableProperty]
        public partial NeteaseAlbum Album { get; set; }
        [ObservableProperty]
        public partial CollectionViewSource AlbumSongsViewSource { get; set; }
        private List<SingleSongBase> _providerAlbumSongs = [];
        [ObservableProperty]
        public partial List<NeteaseArtist> Artists { get; set; }
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

                Album = providerAlbum;
                if (!_setting.noImage) SourceImage = new BitmapImage(new Uri(Album.PictureUrl + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER));
                else SourceImage = new BitmapImage(new Uri("/Assets/icon.png"));

                var artists = providerAlbum.Artists?.ToList();
                AuthorString = string.Join(" / ", artists?.Select(t => t.Name) ?? []);
                Description = (providerAlbum.Alias is { Count: > 0 } ? string.Join(" / ", providerAlbum.Alias) + "\r\n" : string.Empty) + providerAlbum.Description;
                QueueScope = SongListQueueScope.Album(Album.ActualId);
                PublishTime = 0;
                var songs = await LoadAlbumSongsAsync(providerAlbum);
                _providerAlbumSongs = songs;
                var songRows = await Task.WhenAll(songs.Select((song, index) => SongListItemViewModel.FromProviderSongAsync(song, index + 1)));
                AlbumSongsViewSource = new CollectionViewSource()
                {
                    IsSourceGrouped = true,
                    Source = songRows
                    .GroupBy(t => t.CDName).OrderBy(t => t.Key)
                    .Select(t => new SongListItemGroup(t) { Key = t.Key }).ToList()
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
                await _playCore.StopAsync();
                await _playCore.RemoveAllSongAsync();
                if (_providerAlbumSongs.Count > 0)
                    await _playCore.InsertSongRangeAsync(_providerAlbumSongs);
                else
                    await _navigator.AppendAsync(new MusicResource.Album(Album.ActualId));
                await _playCore.MovePointerToIndexAsync(0);
                if (_playCore.CurrentSong is { } song)
                    await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
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
            _navigation.Navigate(typeof(Comments.Comments), CommentTarget.Album(Album.ActualId));
        }

        [RelayCommand]
        private void Subscribe()
        {
            _taskRunner.Forget(Subscribed
                    ? new NeteaseAlbum { ActualId = Album.ActualId, Name = Album.Name }.UnsubscribeAsync()
                    : new NeteaseAlbum { ActualId = Album.ActualId, Name = Album.Name }.SubscribeAsync(),
                "toggle album subscription");
            Subscribed = !Subscribed;
        }

        [RelayCommand]
        private async Task AddAllToPlaylist()
        {
            if (_providerAlbumSongs.Count > 0)
                await _playCore.InsertSongRangeAsync(_providerAlbumSongs);
            else
                await _navigator.AppendAsync(new MusicResource.Album(Album.ActualId));
        }

        private async Task<NeteaseAlbum> LoadProviderAlbumAsync(string albumId)
        {
            if (_providerAlbumTask is not null && _providerAlbumTaskId == albumId)
                return await _providerAlbumTask;

            _providerAlbumTaskId = albumId;
            _providerAlbumTask = LoadProviderAlbumCoreAsync(albumId);
            return await _providerAlbumTask;
        }

        private async Task<NeteaseAlbum> LoadProviderAlbumCoreAsync(string albumId)
        {
            if (await _neteaseProvider.GetAlbumById(albumId) is { } album)
                return album;

            _notification.ShowMessage("获取专辑信息失败", "未能从网易云提供程序加载专辑");
            return null;
        }

        private static async Task<List<SingleSongBase>> LoadAlbumSongsAsync(NeteaseAlbum album)
        {
            List<ProvidableItemBase> items = await album.GetAllItemsAsync();
            return items.OfType<SingleSongBase>().ToList();
        }
    }
}
