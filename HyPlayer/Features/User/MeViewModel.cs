using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
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
        public partial NeteaseUser User { get; set; }

        private readonly IProvidableItemProvidable _itemProvider;
        private readonly Setting _settings;
        private readonly INotificationService _notification;
        private string _loadedUserId = string.Empty;
        private string _initializingUserId = string.Empty;
        private Task _initializeTask;
        private Task _loadPlaylistTask;
        public MeViewModel(IProvidableItemProvidable itemProvider, Setting settings, INotificationService notification)
        {
            _itemProvider = itemProvider;
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
                return await _itemProvider.GetProvidableItemByIdAsync(HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.User + uid);
            });
            User = (NeteaseUser)resp;
            _loadedUserId = uid;
            if (_settings.noImage) User.AvatarUrl = null;
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
                    return await User.GetSubContainerAsync();
                });

                var subListIdx = 0;
                var likedList = new List<SimpleListItem>();
                var myList = new List<SimpleListItem>();
                var playlists = new List<NeteasePlaylist>();
                foreach (var container in val?.OfType<NeteaseUserPlaylistSubContainer>() ?? [])
                {
                    var items = await container.GetAllItemsAsync();
                    playlists.AddRange(items.OfType<NeteasePlaylist>());
                }

                foreach (var valuePlaylist in playlists)
                {
                    if (valuePlaylist.Creator?.ActualId != User.ActualId)
                    {
                        likedList.Add(
                            new SimpleListItem
                            {
                                CoverLink = _settings.noImage ? null : valuePlaylist.CoverUrl,
                                LineOne = valuePlaylist.Creator?.Name,
                                LineThree = $"播放量: {valuePlaylist.PlayCount} | 歌曲数: {valuePlaylist.TrackCount}",
                                LineTwo = valuePlaylist.Description,
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
                                CoverLink = _settings.noImage ? null : valuePlaylist.CoverUrl,
                                LineOne = valuePlaylist.Creator?.Name,
                                LineThree = $"播放量: {valuePlaylist.PlayCount} | 歌曲数: {valuePlaylist.TrackCount}",
                                LineTwo = valuePlaylist.Description,
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

    }
}
