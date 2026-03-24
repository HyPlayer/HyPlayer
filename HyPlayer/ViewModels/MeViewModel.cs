using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.User;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.ViewModels
{
    public partial class MeViewModel : ObservableRecipient
    {
        [ObservableProperty]
        public partial List<SimpleListItem> LikedPlaylist { get; set; }
        [ObservableProperty]
        public partial List<SimpleListItem> MyPlaylist { get; set; }
        [ObservableProperty]
        public partial NCUser User { get; set; }

        private NeteaseCloudMusicApiHandler _neteaseApi;
        private Setting _settings;
        public MeViewModel(NeteaseCloudMusicApiHandler api, Setting settings)
        {
            _neteaseApi = api;
            _settings = settings;
        }
        public async Task InitializeUserInfo(string uid)
        {
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserDetail, uid, async () =>
            {
                var json = await _neteaseApi.RequestAsync(NeteaseApis.UserDetailApi,
                    new UserDetailRequest()
                    {
                        UserId = uid
                    });
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("用户信息获取失败", json.Error?.Message);
                    return null;
                }

                return json.Value;
            });
            User = resp?.Profile?.MapToNcUser();
            if (_settings.noImage) User.Avatar = null;
            LoadPlayList().SafeFireAndForget();
        }
        public async Task LoadPlayList()
        {
            try
            {
                var val = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserPlaylist, User.Id, async () =>
                {
                    var json = await _neteaseApi.RequestAsync(NeteaseApis.UserPlaylistApi,
                        new UserPlaylistRequest()
                        {
                            Uid = User.Id,
                            Limit = 1000 // 为什么这么大, 官方客户端也是这么大
                        });
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("用户歌单获取失败", json.Error?.Message);
                        return null;
                    }

                    return json.Value;
                });

                var subListIdx = 0;
                var likedList = new List<SimpleListItem>();
                var myList = new List<SimpleListItem>();
                foreach (var valuePlaylist in val?.Playlists ?? [])
                {
                    var playList = valuePlaylist.MapToNCPlayList();
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
                                ResourceId = "pl" + playList.PlaylistId,
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
                                ResourceId = "pl" + playList.PlaylistId,
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
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
    }
}
