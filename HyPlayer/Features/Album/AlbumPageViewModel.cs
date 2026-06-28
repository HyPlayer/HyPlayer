using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
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
        private readonly IProvidableItemProvidable _itemProvider;
        private readonly IProviderKnownTypeIds _knownTypeIds;
        private readonly IContainerItemManagementProvidable _containerItemManagement;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private readonly INavigationService _navigation;
        private readonly IAppNavigator _navigator;
        private readonly IBackgroundTaskRunner _taskRunner;
        private string _providerAlbumTaskId;
        private Task<AlbumBase> _providerAlbumTask;

        public AlbumPageViewModel(
            PlayCoreBase playCore,
            IPlaybackControlService control,
            IProvidableItemProvidable itemProvider,
            IProviderKnownTypeIds knownTypeIds,
            IContainerItemManagementProvidable containerItemManagement,
            Setting setting,
            INotificationService notification,
            INavigationService navigation,
            IAppNavigator navigator,
            IBackgroundTaskRunner taskRunner)
        {
            _playCore = playCore;
            _control = control;
            _itemProvider = itemProvider;
            _knownTypeIds = knownTypeIds;
            _containerItemManagement = containerItemManagement;
            _setting = setting;
            _notification = notification;
            _navigation = navigation;
            _navigator = navigator;
            _taskRunner = taskRunner;
            QueueScope = SongListQueueScope.Visible;
        }

        [ObservableProperty]
        public partial AlbumBase Album { get; set; }
        [ObservableProperty]
        public partial CollectionViewSource AlbumSongsViewSource { get; set; }
        private List<SingleSongBase> _providerAlbumSongs = [];
        [ObservableProperty]
        public partial List<PersonBase> Artists { get; set; }
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
                Subscribed = album is IHasLibraryState { IsInCurrentUserLibrary: true };
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
                if (!_setting.noImage && await GetCoverUriAsync(Album) is { } coverUri) SourceImage = new BitmapImage(coverUri);
                else SourceImage = new BitmapImage(new Uri("/Assets/icon.png"));

                var artists = providerAlbum is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync() : null;
                Artists = artists ?? [];
                AuthorString = string.Join(" / ", artists?.Select(t => t.Name) ?? []);
                var aliases = providerAlbum is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
                var description = providerAlbum is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
                Description = (aliases is { Count: > 0 } ? string.Join(" / ", aliases) + "\r\n" : string.Empty) + description;
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
            if (!Subscribed)
            {
                _notification.ShowMessage("暂不支持收藏", "当前抽象只支持从集合中移出项目");
                return;
            }

            _taskRunner.Forget(_containerItemManagement.RemoveItemFromContainerAsync(Album.TypeId, Album.ActualId),
                "remove album from library");
            Subscribed = false;
        }

        [RelayCommand]
        private async Task AddAllToPlaylist()
        {
            if (_providerAlbumSongs.Count > 0)
                await _playCore.InsertSongRangeAsync(_providerAlbumSongs);
            else
                await _navigator.AppendAsync(new MusicResource.Album(Album.ActualId));
        }

        private async Task<AlbumBase> LoadProviderAlbumAsync(string albumId)
        {
            if (_providerAlbumTask is not null && _providerAlbumTaskId == albumId)
                return await _providerAlbumTask;

            _providerAlbumTaskId = albumId;
            _providerAlbumTask = LoadProviderAlbumCoreAsync(albumId);
            return await _providerAlbumTask;
        }

        private async Task<AlbumBase> LoadProviderAlbumCoreAsync(string albumId)
        {
            if (await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.AlbumTypeId + albumId) is AlbumBase album)
                return album;

            _notification.ShowMessage("获取专辑信息失败", "未能从提供程序加载专辑");
            return null;
        }

        private static async Task<List<SingleSongBase>> LoadAlbumSongsAsync(AlbumBase album)
        {
            List<ProvidableItemBase> items = album switch
            {
                IProgressiveLoadingContainer progressive => await LoadAllProgressiveItemsAsync(progressive),
                _ => []
            };
            return items.OfType<SingleSongBase>().ToList();
        }

        private static async Task<List<ProvidableItemBase>> LoadAllProgressiveItemsAsync(IProgressiveLoadingContainer container)
        {
            var items = new List<ProvidableItemBase>();
            var offset = 0;
            var count = container.MaxProgressiveCount;
            var hasMore = true;

            while (hasMore)
            {
                var result = await container.GetProgressiveItemsListAsync(offset, count);
                hasMore = result.Item1;
                if (result.Item2.Count == 0)
                    break;

                items.AddRange(result.Item2);
                offset += result.Item2.Count;
            }

            return items;
        }

        private static async Task<Uri?> GetCoverUriAsync(AlbumBase album)
        {
            if (album is not IHasCover coverProvider)
                return null;

            var result = await coverProvider.GetCoverAsync();
            return result is IResourceResultOf<Uri?> uriResult ? await uriResult.GetResourceAsync() : null;
        }
    }
}
