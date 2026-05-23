using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
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
        public partial NCUser User { get; set; }

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
            User = MapUser(resp);
            if (_settings.noImage) User.Avatar = null;
            LoadPlayList().SafeFireAndForget();
        }
        public async Task LoadPlayList()
        {
            try
            {
                var val = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserPlaylist, User.Id, async () =>
                {
                    var userItem = await _itemProvider.GetProvidableItemByIdAsync(HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.User + User.Id);
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
                    var playList = MapPlaylist(valuePlaylist);
                    if (playList.Creator.Id != User.Id)
                    {
                        likedList.Add(
                            new SimpleListItem
                            {
                                CoverLink = _settings.noImage ? null : playList.Cover,
                                LineOne = playList.Creator.Name,
                                LineThree = $"播放量: {playList.PlayCount} | 歌曲数: {playList.TrackCount}",
                                LineTwo = playList.Description,
                                Order = subListIdx++,
                                Route = new AppRoute.Playlist($"{playList.PlaylistId}"),
                                PlayResource = new MusicResource.Playlist($"{playList.PlaylistId}"),
                                Title = playList.Name,
                                CanPlay = true
                            }
                        );
                    }
                    else
                    {
                        myList.Add(
                            new SimpleListItem
                            {
                                CoverLink = _settings.noImage ? null : playList.Cover,
                                LineOne = playList.Creator.Name,
                                LineThree = $"播放量: {playList.PlayCount} | 歌曲数: {playList.TrackCount}",
                                LineTwo = playList.Description,
                                Order = subListIdx++,
                                Route = new AppRoute.Playlist($"{playList.PlaylistId}"),
                                PlayResource = new MusicResource.Playlist($"{playList.PlaylistId}"),
                                Title = playList.Name,
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

        private static NCUser MapUser(HyPlayer.PlayCore.Abstraction.Models.ProvidableItemBase? item)
        {
            if (item is NeteaseUser user)
            {
                return new NCUser
                {
                    Avatar = user.AvatarUrl,
                    Id = user.ActualId,
                    Name = user.Name,
                    Signature = user.Description
                };
            }

            return new NCUser { Id = item?.ActualId ?? string.Empty, Name = item?.Name ?? string.Empty };
        }

        private static NCPlayList MapPlaylist(NeteasePlaylist playlist)
        {
            return new NCPlayList
            {
                Cover = playlist.CoverUrl,
                Creator = new NCUser
                {
                    Avatar = playlist.Creator?.AvatarUrl,
                    Id = playlist.Creator?.ActualId,
                    Name = playlist.Creator?.Name,
                    Signature = playlist.Creator?.Description
                },
                Description = playlist.Description,
                Name = playlist.Name,
                PlaylistId = playlist.ActualId,
                HasSubscribed = playlist.Subscribed,
                PlayCount = playlist.PlayCount,
                TrackCount = playlist.TrackCount,
                BookCount = playlist.SubscribedCount,
                UpdateTime = HyPlayer.UI.Converters.DateConverter.GetDateTimeFromTimeStamp(playlist.UpdateTime)
            };
        }
    }
}
