using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.User;
using HyPlayer.Infrastructure.Imaging;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.UI.Lists;
using HyPlayer.Services.Downloads;
using HyPlayer.UI.Converters;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;

namespace HyPlayer.Features.Playlist
{
    public partial class SongListViewModel : ObservableRecipient
    {
        private readonly IPlaylistService _playlist;
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private readonly INavigationService _navigation;
        private readonly IAppNavigator _navigator;
        private readonly HttpClient _httpClient;
        private readonly IGlobalTimerService _globalTimer;
        private readonly WeakEventListener<SongListViewModel, object?, EventArgs> _secondTickListener;
        private bool _isSecondTickSubscribed;

        public SongListViewModel(
            IPlaylistService playlist,
            global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
            Setting setting,
            INotificationService notification,
            INavigationService navigation,
            IAppNavigator navigator,
            HttpClient httpClient,
            IGlobalTimerService globalTimer)
        {
            _playlist = playlist;
            _neteaseProvider = neteaseProvider;
            _setting = setting;
            _notification = notification;
            _navigation = navigation;
            _navigator = navigator;
            _httpClient = httpClient;
            _globalTimer = globalTimer;
            _secondTickListener = new WeakEventListener<SongListViewModel, object?, EventArgs>(this)
            {
                OnEventAction = static (instance, _, _) => instance.GreedlyLoad(),
                OnDetachAction = weakEventListener => { _globalTimer.SecondTick -= weakEventListener.OnEvent; }
            };
            QueueScope = SongListQueueScope.Visible;
        }

        public ObservableCollection<SongListItemViewModel> Songs { get; set; } = [];
        private readonly List<SingleSongBase> _dailyRecommendProviderSongs = [];
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
        public partial string? UpdateTime { get; set; }
        [ObservableProperty]
        public partial Uri? CoverUri { get; set; }
        [ObservableProperty]
        public partial Color AlbumColor { get; set; }
        [ObservableProperty]
        public partial SongListQueueScope QueueScope { get; set; }
#nullable restore

        private NeteasePlaylist _neteasePlaylist;
        private int _greedyLoadTreashold = 3;

        public async Task LoadPageData(string PlaylistId, bool loadPlaylist = false)
        {
            QueueScope = SongListQueueScope.Playlist(PlaylistId);
            if (loadPlaylist)
            {
                _neteasePlaylist = await _neteaseProvider.GetPlaylistById(PlaylistId);
                if (_neteasePlaylist is null)
                {
                    _notification.ShowMessage("加载歌单出错", "未找到歌单信息");
                    return;
                }

                PlayList = MapToNCPlayList(_neteasePlaylist);
            }

            DescriptionBoxContent = PlayList.Description;
            if (_setting.noImage)
            {
                CoverUri = null;
            }
            else
            {
                CoverUri = PlayList.IsDailyRecommend ? new Uri(PlayList.Cover) : new Uri(PlayList.Cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER);
            }
            LoadAlbumImage().SafeFireAndForget();
            UpdateTime = PlayList.UpdateTime == DateTime.MinValue ? string.Empty : $"{DateConverter.FriendFormat(PlayList.UpdateTime)}更新";
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
            if (_setting.greedlyLoadPlayContainerItems)
                AttachSecondTick();
            IsLoading = false;
        }

        private void AttachSecondTick()
        {
            if (_isSecondTickSubscribed) return;
            _globalTimer.SecondTick += _secondTickListener.OnEvent;
            _isSecondTickSubscribed = true;
        }

        private void DetachSecondTick()
        {
            if (!_isSecondTickSubscribed) return;
            _secondTickListener.Detach();
            _isSecondTickSubscribed = false;
        }

        public async Task LoadDailyRcmdItems()
        {
            QueueScope = SongListQueueScope.Content;
            var items = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "recommendSongs", async () =>
            {
                try
                {
                    return (await LoadContainerItemsAsync(new NeteaseRecommendSongContainer { ActualId = "rcsg", Name = "推荐歌曲" }))
                        .OfType<SingleSongBase>()
                        .ToList();
                }
                catch (Exception ex)
                {
                    _notification.ShowMessage("加载日推出错", ex.Message);
                    return null;
                }
            }, TimeSpan.FromDays(1));

            _dailyRecommendProviderSongs.Clear();
            _dailyRecommendProviderSongs.AddRange(items ?? []);
            var idx = 0;
            foreach (var song in _dailyRecommendProviderSongs)
            {
                Songs.Add(await SongListItemViewModel.FromProviderSongAsync(song, idx++));
            }
            HasMore = false;
        }

        public async Task LoadPlayListItems()
        {
            _neteasePlaylist ??= await _neteaseProvider.GetPlaylistById(PlayList.PlaylistId);
            if (_neteasePlaylist is null)
            {
                _notification.ShowMessage("加载歌单出错", "未找到歌单信息");
                return;
            }

            PlayList = MapToNCPlayList(_neteasePlaylist);
            if (_neteasePlaylist.IsNewImported &&
                _neteasePlaylist.Creator?.ActualId == Ioc.Default.GetRequiredService<IAuthService>().CurrentUser?.Id)
            {
                IntelligenceModeVisible = true;
                IsMySongList = true;
            }
        }

        public async Task LoadCurrentPage()
        {
            _neteasePlaylist ??= await _neteaseProvider.GetPlaylistById(PlayList.PlaylistId);
            if (_neteasePlaylist is null)
            {
                _notification.ShowMessage("加载歌单歌曲出错", "未找到歌单信息");
                return;
            }

            (bool hasMore, List<ProvidableItemBase> items) rst;
            try
            {
                rst = await _neteasePlaylist.GetProgressiveItemsListAsync(CurrentPage * 500, 500);
            }
            catch (Exception ex)
            {
                _notification.ShowMessage("加载歌单歌曲出错", ex.Message);
                return;
            }

            var idx = CurrentPage * 500;
            foreach (var song in rst.items.OfType<SingleSongBase>())
            {
                Songs.Add(await SongListItemViewModel.FromProviderSongAsync(song, idx++));
            }
            HasMore = rst.hasMore;
        }

        public async Task LoadAlbumImage()
        {
            using var result = await _httpClient.GetAsync(new Uri(PlayList.Cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
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
                DetachSecondTick();
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
                _playlist.AppendItems(GetDailyRecommendProviderSongs(), clearFirst: false);
            }
        }
        [RelayCommand]
        private void NavigateToComments()
        {
            _navigation.Navigate(typeof(Comments.Comments), CommentTarget.Playlist(PlayList.PlaylistId));
        }
        [RelayCommand]
        private async Task ResetCacheAsync()
        {
            try
            {
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracks, PlayList.PlaylistId);
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracksDetail, PlayList.PlaylistId, true);
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistDetail, PlayList.PlaylistId);
                _notification.ShowMessage("清除缓存成功", "已清除当前歌单的缓存");
                Songs.Clear();
                CurrentPage = 0;
                _neteasePlaylist = null;
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
                await _navigator.AppendAsync(new MusicResource.Playlist(PlayList.PlaylistId));
                await _playlist.MoveNextAsync(userInitiated: true);
            }
            else
            {
                _playlist.AppendItems(GetDailyRecommendProviderSongs(), clearFirst: true);
                _navigator.SetPlaybackSource(new MusicResource.DailyRecommend(PlayList.PlaylistId));
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
            if (PlayList.IsDailyRecommend && HasCompleteDailyRecommendProviderSongs())
                DownloadManager.AddDownload(_dailyRecommendProviderSongs);
            else
                DownloadManager.AddDownload(Songs.Select(song => song.ToProviderSong()).ToList());
        }

        private bool HasCompleteDailyRecommendProviderSongs()
        {
            return _dailyRecommendProviderSongs.Count > 0 && _dailyRecommendProviderSongs.Count == Songs.Count;
        }

        private IEnumerable<SingleSongBase> GetDailyRecommendProviderSongs()
        {
            return HasCompleteDailyRecommendProviderSongs()
                ? _dailyRecommendProviderSongs
                : Songs.Select(song => song.ToProviderSong());
        }

        [RelayCommand]
        private async Task LikePlaylist()
        {
            try
            {
                var playlist = new NeteasePlaylist { ActualId = PlayList.PlaylistId, Name = PlayList.Name };
                if (PlayList.HasSubscribed)
                {
                    await playlist.UnsubscribeAsync();
                }
                else
                {
                    await playlist.SubscribeAsync();
                }
                PlayList.HasSubscribed = !PlayList.HasSubscribed;
            }
            catch (Exception ex)
            {
                _notification.ShowMessage("操作失败", ex.Message);
            }
        }

        private static async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(ContainerBase container)
        {
            return container switch
            {
                IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0, progressive.MaxProgressiveCount)).Item2,
                LinerContainerBase liner => await liner.GetAllItemsAsync(),
                UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(),
                _ => []
            };
        }

        private static NCPlayList MapToNCPlayList(NeteasePlaylist playlist)
        {
            return new NCPlayList
            {
                PlaylistId = playlist.ActualId ?? string.Empty,
                Name = playlist.Name,
                Description = playlist.Description,
                Cover = playlist.CoverUrl,
                Creator = playlist.Creator is null
                    ? new NCUser()
                    : new NCUser
                    {
                        Id = playlist.Creator.ActualId ?? string.Empty,
                        Name = playlist.Creator.Name,
                        Avatar = string.Empty,
                        Signature = string.Empty
                    },
                HasSubscribed = playlist.Subscribed,
                TrackCount = playlist.TrackCount,
                PlayCount = playlist.PlayCount,
                BookCount = playlist.SubscribedCount,
                UpdateTime = playlist.UpdateTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(playlist.UpdateTime).LocalDateTime : DateTime.MinValue
            };
        }

        [RelayCommand]
        private void NavigateToAuthor()
        {
            _navigation.Navigate(typeof(Me), PlayList.Creator.Id);
        }
    }
}
