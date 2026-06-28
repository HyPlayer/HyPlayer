using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Features.Album
{
    public partial class AlbumPageViewModel : ObservableRecipient
    {
        private readonly IProvidableItemProvidable _itemProvider;
        private readonly IProviderKnownTypeIds _knownTypeIds;
        private readonly IContainerItemManagementProvidable _containerItemManagement;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private readonly INavigationService _navigation;
        private readonly IBackgroundTaskRunner _taskRunner;
        private string _providerAlbumTaskId;
        private Task<AlbumBase> _providerAlbumTask;

        public AlbumPageViewModel(
            IProvidableItemProvidable itemProvider,
            IProviderKnownTypeIds knownTypeIds,
            IContainerItemManagementProvidable containerItemManagement,
            Setting setting,
            INotificationService notification,
            INavigationService navigation,
            IBackgroundTaskRunner taskRunner)
        {
            _itemProvider = itemProvider;
            _knownTypeIds = knownTypeIds;
            _containerItemManagement = containerItemManagement;
            _setting = setting;
            _notification = notification;
            _navigation = navigation;
            _taskRunner = taskRunner;
        }

        [ObservableProperty]
        public partial AlbumBase Album { get; set; }
        [ObservableProperty]
        public partial List<PersonBase> Artists { get; set; }
        [ObservableProperty]
        public partial string AuthorString { get; set; }
        [ObservableProperty]
        public partial string Description { get; set; }
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
                PublishTime = 0;
            }
            catch (Exception ex)
            {
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
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

        private static async Task<Uri?> GetCoverUriAsync(AlbumBase album)
        {
            if (album is not IHasCover coverProvider)
                return null;

            var result = await coverProvider.GetCoverAsync();
            return result is IResourceResultOf<Uri?> uriResult ? await uriResult.GetResourceAsync() : null;
        }
    }
}
