using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace HyPlayer.ViewModels
{
    public partial class SongListViewModel : ObservableRecipient
    {
        private readonly IPlaylistService _playlist;

        public SongListViewModel(IPlaylistService playlist)
        {
            _playlist = playlist;
        }

        public ObservableCollection<NCSong> Songs { get; set; } = [];
        [ObservableProperty]
        public partial NCPlayList PlayList { get; set; }
        [ObservableProperty]
        public partial int CurrentPage { get; set; }
        [ObservableProperty]
        public partial bool HasMore { get; set; }
        [ObservableProperty]
        public partial bool IntelligenceModeVisible { get; set; }
        [ObservableProperty]
        public partial bool IsMySongList { get; set; }
        [ObservableProperty]
        public partial bool IsLoading { get; set; }
        [ObservableProperty]
        public partial string DescriptionBoxContent { get; set; }
#nullable enable
        [ObservableProperty]
        public partial string? ResourceId { get; set; }
        [ObservableProperty]
        public partial string? UpdateTime { get; set; }
        [ObservableProperty]
        public partial Uri? CoverUri { get; set; }
        [ObservableProperty]
        public partial Color AlbumColor { get; set; }
        [ObservableProperty]
        public partial string SourceId {  get; set; }
#nullable restore

        private List<string> _songListIds = [];
        private int _greedyLoadTreashold = 3;
        private int _greedyLoadCooldownTime = 0;

        public async Task LoadPageData(string PlaylistId, bool loadPlaylist = false)
        {
            if (loadPlaylist)
            {
                var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistDetail, PlaylistId, async () =>
                {
                    SourceId = $"pl{PlaylistId}";
                    // 歌单详情
                    var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistDetailApi,
                        new PlaylistDetailRequest()
                        {
                            Id = PlaylistId
                        });
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("加载歌单出错", json.Error?.Message ?? "未知错误");
                        return null;
                    }

                    return json.Value;
                });

                PlayList = rst?.Playlists?.FirstOrDefault().MapToNCPlayList();
            }
            DescriptionBoxContent = PlayList.Description;
            ResourceId = "pl" + PlayList?.PlaylistId;
            if (Common.Setting.noImage)
            {
                CoverUri = null;
            }
            else
            {
                CoverUri = PlayList.IsDailyRecommend ? new Uri(PlayList.Cover) : new Uri(PlayList.Cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER);
            }
            LoadAlbumImage().SafeFireAndForget();
            UpdateTime = $"{DateConverter.FriendFormat(PlayList.UpdateTime)}更新";
            LoadSongListItem().SafeFireAndForget();
        }

        public async Task LoadSongListItem()
        {
            IsLoading = true;
            if (!PlayList.IsDailyRecommend)
            {
                await LoadPlayListItems();
                await LoadCurrentPage();
            }
            else
            {
                IntelligenceModeVisible = false;
                await LoadDailyRcmdItems();
            }
            if (Common.Setting.greedlyLoadPlayContainerItems)
                // Use WeakReferenceMessenger for timer-tick driven greedy loading
                WeakReferenceMessenger.Default.Register<PositionTickMessage>(this, (r, _) => ((SongListViewModel)r).GreedlyLoad());
            IsLoading = false;
        }

        public async Task LoadDailyRcmdItems()
        {
            ResourceId = "content";
            var items = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "recommendSongs", async () =>
            {
                // 每天推荐歌曲
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.RecommendSongsApi);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载日推出错", json.Error?.Message);
                    return null;
                }
                return json.Value;
            }, TimeSpan.FromDays(1));

            if (items?.Data?.DailySongs?.FirstOrDefault()?.RecommendReason == "birthDaySong")
            {
                // 诶呀,没想到还过生了,吼吼
                DescriptionBoxContent = "生日快乐~ 今天也要开心哦!";
            }

            var idx = 0;
            foreach (var song in items?.Data?.DailySongs ?? [])
            {
                var ncSong = song.MapNcSong();
                ncSong.IsAvailable = true;
                ncSong.Order = idx++;
                Songs.Add(ncSong);
            }
            HasMore = false;
        }

        public async Task LoadPlayListItems()
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracks, PlayList.PlaylistId, async () =>
            {
                // 歌单详情
                var rst = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistTracksGetApi,
                    new PlaylistTracksGetRequest()
                    {
                        Id = PlayList.PlaylistId
                    });
                if (rst.IsError)
                {
                    Common.AddToTeachingTipLists("加载歌单出错", rst.Error?.Message ?? "未知错误");
                    return null;
                }
                return rst.Value;
            });

            var playlistDetail = json?.Playlist?.TrackIds;
            if (playlistDetail is null)
            {
                Common.AddToTeachingTipLists("加载歌单出错", "未找到歌单信息");
                return;
            }
            if (json.Playlist.SpecialType == 5 &&
                json.Playlist.Creator?.UserId == Common.LoginedUser?.Id)
            {
                IntelligenceModeVisible = true;
                IsMySongList = true;
            }
            _songListIds = playlistDetail.Select(x => x.Id).ToList();
        }

        public async Task LoadCurrentPage()
        {

            var trackIds = _songListIds.Skip(CurrentPage * 500).Take(500).ToList();
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracksDetail, PlayList.PlaylistId + "_" + CurrentPage, async () =>
            {
                // 歌单歌曲详情
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                    new SongDetailRequest()
                    {
                        IdList = trackIds
                    });
                if (json is { IsError: true, Error.ErrorCode: 405 })
                {
                    _greedyLoadTreashold = ++_greedyLoadCooldownTime * 10;
                    CurrentPage--;
                    Common.AddToTeachingTipLists("贪婪加载被风控", $"渐进加载速度过于快, 将在 {_greedyLoadCooldownTime * 10} 秒后尝试继续加载, 正在清洗请求");
                    return null;
                }
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载歌单歌曲出错", json.Error?.Message ?? "未知错误");
                    return null;
                }
                return json.Value;
            });

            if (rst is null)
            {
                return;
            }
            var idx = CurrentPage * 500;
            foreach (var jToken in rst.Songs ?? [])
            {
                var ncSong = jToken.MapToNcSong();
                ncSong.Order = idx++;
                Songs.Add(ncSong);
            }
            if (_songListIds.Count < Songs.Count)
            {
                HasMore = true;
            }
        }

        public async Task LoadAlbumImage()
        {
            using var result = await Common.HttpClient!.GetAsync(new Uri(PlayList.Cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
            if (result.IsSuccessStatusCode)
            {
                using var stream = await result.Content.ReadAsStreamAsync();
                using var inputStream = stream.AsRandomAccessStream();
                Color imageMainColor = await ColorExtractor.ExtractColorFromStream(inputStream);
                AlbumColor = imageMainColor;
            }
        }

        public void GreedlyLoad()
        {
            if (HasMore && _greedyLoadTreashold-- <= 0)
            {
                LoadCurrentPage()?.SafeFireAndForget();
                _greedyLoadTreashold = 3;
            }
            else if (HasMore == false)
            {
                // Unregister greedy-load tick handler
                WeakReferenceMessenger.Default.Unregister<PositionTickMessage>(this);
            }
        }
        [RelayCommand]
        private void LoadAllSongs()
        {
            if (!PlayList.IsDailyRecommend)
            {
                _playlist.AppendPlayListAsync(PlayList.PlaylistId).SafeFireAndForget();
            }
            else
            {
                var items = Songs.Select(s => _playlist.NCSongToPlayItem(s));
                _playlist.AppendItems(items);
                _playlist.NotifyAppendDone();
            }
        }
        [RelayCommand]
        private void NavigateToComments()
        {
            Common.NavigatePage(typeof(Comments), "pl" + PlayList.PlaylistId);
        }
        [RelayCommand]
        private async Task ResetCacheAsync()
        {
            try
            {
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracks, PlayList.PlaylistId);
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracksDetail, PlayList.PlaylistId, true);
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistDetail, PlayList.PlaylistId);
                Common.AddToTeachingTipLists("清除缓存成功", "已清除当前歌单的缓存");
                Songs.Clear();
                CurrentPage = 0;
                LoadSongListItem().SafeFireAndForget();

            }
            catch
            {
                // ignore
            }
        }

        [RelayCommand]
        private async Task PlayAllAsync()
        {
            if (!PlayList.IsDailyRecommend)
            {
                _playlist.Clear();
                await _playlist.AppendPlayListAsync(PlayList.PlaylistId);
                _playlist.PlaySourceId = $"pl{PlayList.PlaylistId}";
                await _playlist.MoveNextAsync(userInitiated: true);
            }
            else
            {
                _playlist.Clear();
                var items = Songs.Select(s => _playlist.NCSongToPlayItem(s));
                _playlist.AppendItems(items);
                _playlist.PlaySourceId = $"{PlayList.PlaylistId}";
                _playlist.NotifyAppendDone();
                await _playlist.MoveNextAsync(userInitiated: true);
            }
        }

        [RelayCommand]
        private void NextPage()
        {
            CurrentPage++;
            LoadCurrentPage().SafeFireAndForget();
        }

        [RelayCommand]
        private void EnterIntelligencePlay()
        {
            Api.EnterIntelligencePlay().SafeFireAndForget();
        }

        [RelayCommand]
        private void DownloadAll()
        {
            DownloadManager.AddDownload(Songs.ToList());
        }

        [RelayCommand]
        private async Task LikePlaylist()
        {
            var result = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistSubscribeApi,
                new PlaylistSubscribeRequest()
                {
                    PlaylistId = PlayList.PlaylistId,
                    IsSubscribe = !PlayList.HasSubscribed
                });
            if (result.IsError)
            {
                Common.AddToTeachingTipLists("操作失败", result.Error.Message);
                return;
            }
            PlayList.HasSubscribed = !PlayList.HasSubscribed;
        }

        [RelayCommand]
        private void NavigateToAuthor()
        {
            Common.NavigatePage(typeof(Me), PlayList.Creator.Id);
        }
    }
}
