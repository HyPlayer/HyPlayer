using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Features.User
{
    public partial class MeViewModel : ObservableRecipient
    {
        [ObservableProperty]
        public partial List<SimpleListItem> LikedPlaylist { get; set; }
        [ObservableProperty]
        public partial List<SimpleListItem> MyPlaylist { get; set; }
        [ObservableProperty]
        public partial UserProfileViewData User { get; set; }

        private readonly IProvidableItemProvidable _itemProvider;
        private readonly IProviderKnownTypeIds _knownTypeIds;
        private readonly Setting _settings;
        private readonly INotificationService _notification;
        private PersonBase _providerUser;
        private string _loadedUserId = string.Empty;
        private string _initializingUserId = string.Empty;
        private Task _initializeTask;
        private Task _loadPlaylistTask;
        public MeViewModel(IProvidableItemProvidable itemProvider, IProviderKnownTypeIds knownTypeIds, Setting settings, INotificationService notification)
        {
            _itemProvider = itemProvider;
            _knownTypeIds = knownTypeIds;
            _settings = settings;
            _notification = notification;
        }
        public Task InitializeUserInfo(string uid)
        {
            if (_loadedUserId == uid && User is not null)
                return Task.CompletedTask;

            if (_initializingUserId == uid && _initializeTask is not null && !_initializeTask.IsCompleted)
                return _initializeTask;

            _initializingUserId = uid;
            _initializeTask = InitializeUserInfoCoreAsync(uid);
            return _initializeTask;
        }

        private async Task InitializeUserInfoCoreAsync(string uid)
        {
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserDetail, uid, async () =>
            {
                return await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.UserTypeId + uid);
            });
            if (resp is not PersonBase user)
                return;

            _providerUser = user;
            User = await CreateUserProfileViewDataAsync(user);
            _loadedUserId = uid;
            _loadPlaylistTask = LoadPlayListCoreAsync();
            await _loadPlaylistTask;
        }
        public async Task LoadPlayList()
        {
            if (_loadPlaylistTask is not null && !_loadPlaylistTask.IsCompleted)
            {
                await _loadPlaylistTask;
                return;
            }

            _loadPlaylistTask = LoadPlayListCoreAsync();
            await _loadPlaylistTask;
        }

        private async Task LoadPlayListCoreAsync()
        {
            try
            {
                var val = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserPlaylist, User.ActualId, async () =>
                {
                    return await _providerUser.GetSubContainerAsync();
                });

                var subListIdx = 0;
                var likedList = new List<SimpleListItem>();
                var myList = new List<SimpleListItem>();
                var playlists = new List<ContainerBase>();
                foreach (var container in val?.OfType<ContainerBase>() ?? [])
                {
                    if (container.TypeId != _knownTypeIds.PlaylistTypeId)
                        continue;

                    var items = await LoadPlaylistContainerItemsAsync(container);
                    playlists.AddRange(items.OfType<ContainerBase>());
                }

                foreach (var valuePlaylist in playlists)
                {
                    var creators = valuePlaylist is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync() : null;
                    var owner = creators?.FirstOrDefault();
                    var description = valuePlaylist is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
                    var coverLink = _settings.noImage ? null : await TryGetCoverLinkAsync(valuePlaylist);
                    var isOwned = valuePlaylist is IHasLibraryState libraryState
                        ? libraryState.IsOwnedByCurrentUser
                        : owner?.ActualId == User.ActualId;
                    if (!isOwned)
                    {
                        likedList.Add(
                            new SimpleListItem
                            {
                                CoverLink = coverLink,
                                LineOne = owner?.Name,
                                LineThree = string.Empty,
                                LineTwo = description,
                                Order = subListIdx++,
                                Route = new AppRoute.Playlist($"{valuePlaylist.ActualId}"),
                                PlayResource = new MusicResource.Playlist($"{valuePlaylist.ActualId}"),
                                Title = valuePlaylist.Name,
                                CanPlay = true
                            }
                        );
                    }
                    else
                    {
                        myList.Add(
                            new SimpleListItem
                            {
                                CoverLink = coverLink,
                                LineOne = owner?.Name,
                                LineThree = string.Empty,
                                LineTwo = description,
                                Order = subListIdx++,
                                Route = new AppRoute.Playlist($"{valuePlaylist.ActualId}"),
                                PlayResource = new MusicResource.Playlist($"{valuePlaylist.ActualId}"),
                                Title = valuePlaylist.Name,
                                CanPlay = true
                            }
                        );
                    }
                }
                LikedPlaylist = likedList;
                MyPlaylist = myList;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException or TaskCanceledException))
            {
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        private static async Task<List<ProvidableItemBase>> LoadPlaylistContainerItemsAsync(ContainerBase container)
        {
            return container switch
            {
                LinerContainerBase liner => await liner.GetAllItemsAsync(),
                IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0, progressive.MaxProgressiveCount)).Item2,
                _ => []
            };
        }

        private async Task<UserProfileViewData> CreateUserProfileViewDataAsync(PersonBase user)
        {
            return new UserProfileViewData
            {
                ActualId = user.ActualId,
                Name = user.Name,
                Description = user is IHasDescription descriptionProvider ? descriptionProvider.Description : null,
                AvatarUrl = _settings.noImage ? null : await TryGetCoverLinkAsync(user)
            };
        }

        private static async Task<string?> TryGetCoverLinkAsync(ProvidableItemBase item)
        {
            if (item is not IHasCover coverProvider)
                return null;

            var result = await coverProvider.GetCoverAsync();
            return result is IResourceResultOf<Uri?> uriResult
                ? (await uriResult.GetResourceAsync())?.GetLeftPart(UriPartial.Path)
                : null;
        }

        public sealed class UserProfileViewData
        {
            public string? ActualId { get; init; }
            public string? Name { get; init; }
            public string? Description { get; init; }
            public string? AvatarUrl { get; init; }
        }
    }
}
