using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
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

        private IProvidableItemProvidable _itemProvider;
        private Setting _settings;
        private readonly INotificationService _notification;
        public MeViewModel(IProvidableItemProvidable itemProvider, Setting settings, INotificationService notification)
        {
            _itemProvider = itemProvider;
            _settings = settings;
            _notification = notification;
        }
        public async Task InitializeUserInfo(string uid)
        {
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserDetail, uid, async () =>
            {
                return await _itemProvider.GetProvidableItemByIdAsync(HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.User + uid);
            });
            User = (NeteaseUser)resp;
            if (_settings.noImage) User.AvatarUrl = null;
            LoadPlayList().SafeFireAndForget();
        }
        public async Task LoadPlayList()
        {
            try
            {
                var val = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserPlaylist, User.ActualId, async () =>
                {
                    var userItem = await _itemProvider.GetProvidableItemByIdAsync(HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.User + User.ActualId);
                    return userItem is ContainersContainer containersContainer
                        ? await containersContainer.GetSubContainerAsync()
                        : [];
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
