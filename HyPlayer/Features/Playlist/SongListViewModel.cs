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
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
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
        private readonly PlayCoreBase _playCore;
        private readonly IPlaybackQueueLoader _queueLoader;
        private readonly IPlaybackControlService _control;
        private readonly IProvidableItemProvidable _itemProvider;
        private readonly IProviderKnownTypeIds _knownTypeIds;
        private readonly IProviderSpecialContainerTypeIds _specialContainerTypeIds;
        private readonly IContainerItemManagementProvidable _containerItemManagement;
        private readonly Setting _setting;
        private readonly INotificationService _notification;
        private readonly INavigationService _navigation;
        private readonly IAppNavigator _navigator;
        private readonly HttpClient _httpClient;
        private readonly IGlobalTimerService _globalTimer;
        private readonly WeakEventListener<SongListViewModel, object?, EventArgs> _secondTickListener;
        private bool _isSecondTickSubscribed;

        public SongListViewModel(
            PlayCoreBase playCore,
            IPlaybackQueueLoader queueLoader,
            IPlaybackControlService control,
            IProvidableItemProvidable itemProvider,
            IProviderKnownTypeIds knownTypeIds,
            IProviderSpecialContainerTypeIds specialContainerTypeIds,
            IContainerItemManagementProvidable containerItemManagement,
            Setting setting,
            INotificationService notification,
            INavigationService navigation,
            IAppNavigator navigator,
            HttpClient httpClient,
            IGlobalTimerService globalTimer)
        {
            _playCore = playCore;
            _queueLoader = queueLoader;
            _control = control;
            _itemProvider = itemProvider;
            _knownTypeIds = knownTypeIds;
            _specialContainerTypeIds = specialContainerTypeIds;
            _containerItemManagement = containerItemManagement;
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
        public partial ContainerBase PlayList { get; set; }
        [ObservableProperty]
        public partial bool IsDailyRecommend { get; set; }
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
        [ObservableProperty]
        public partial string CreatorName { get; set; }
        [ObservableProperty]
        public partial string? CreatorId { get; set; }
        [ObservableProperty]
        public partial bool Subscribed { get; set; }
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

        private ContainerBase _playlistContainer;
        private IProgressiveLoadingContainer _progressivePlaylist;
        private int _greedyLoadTreashold = 3;
        private readonly HashSet<int> _loadedPages = [];
        private string _loadedPlaylistId;
        private bool _loadedDailyRecommend;
        private bool _loadedPlaylistDetail;
        private Task _loadSongListTask;
        private Task _loadAlbumImageTask;
        private string _loadedAlbumImageUrl;

        public async Task LoadPageData(string PlaylistId, bool loadPlaylist = false)
        {
            QueueScope = SongListQueueScope.Playlist(PlaylistId);
            var playlistChanged = !string.Equals(_loadedPlaylistId, PlaylistId, StringComparison.Ordinal)
                                  || _loadedDailyRecommend != IsDailyRecommend;
            if (playlistChanged)
            {
                DetachSecondTick();
                Songs.Clear();
                _dailyRecommendProviderSongs.Clear();
                _loadedPages.Clear();
                _playlistContainer = null;
                _progressivePlaylist = null;
                _loadSongListTask = null;
                _loadAlbumImageTask = null;
                _loadedAlbumImageUrl = null;
                _loadedPlaylistDetail = false;
                _loadedDailyRecommend = false;
                CurrentPage = 0;
                HasMore = false;
                IntelligenceModeVisible = false;
                IsMySongList = false;
            }

            _loadedPlaylistId = PlaylistId;

            if (loadPlaylist && (playlistChanged || PlayList?.ActualId != PlaylistId || _playlistContainer?.ActualId != PlaylistId))
            {
                _playlistContainer = await LoadProviderPlaylistAsync(PlaylistId);
                if (_playlistContainer is null)
                {
                    _notification.ShowMessage("加载歌单出错", "未找到歌单信息");
                    return;
                }

                PlayList = _playlistContainer;
                _loadedPlaylistDetail = false;
            }

            DescriptionBoxContent = PlayList is IHasDescription descriptionProvider ? descriptionProvider.Description ?? string.Empty : string.Empty;
            await LoadCreatorAsync(PlayList);
            Subscribed = PlayList is IHasLibraryState { IsInCurrentUserLibrary: true };
            if (_setting.noImage)
            {
                CoverUri = null;
            }
            else
            {
                CoverUri = await GetCoverUriAsync(PlayList);
            }
            StartAlbumImageLoad();
            UpdateTime = string.Empty;
            _loadSongListTask ??= LoadSongListItem();
            _loadSongListTask.SafeFireAndForget();
        }

        public async Task LoadSongListItem()
        {
            IsLoading = true;
            if (!IsDailyRecommend)
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
            if (_loadedDailyRecommend && _dailyRecommendProviderSongs.Count == Songs.Count && Songs.Count > 0)
                return;

            QueueScope = SongListQueueScope.Content;
            var items = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "recommendSongs", async () =>
            {
                try
                {
                    return (await LoadDailyRecommendContainerItemsAsync())
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
            Songs.Clear();
            var idx = 0;
            foreach (var song in _dailyRecommendProviderSongs)
            {
                Songs.Add(await SongListItemViewModel.FromProviderSongAsync(song, idx++));
            }
            _loadedDailyRecommend = true;
            HasMore = false;
        }

        public async Task LoadPlayListItems()
        {
            if (_loadedPlaylistDetail)
                return;

            if (!await EnsurePlaylistLoadedAsync("加载歌单出错"))
                return;

            PlayList = _playlistContainer;
            var auth = Ioc.Default.GetRequiredService<IAuthService>();
            if (IsLikedMusicPlaylist(_playlistContainer, auth))
            {
                IntelligenceModeVisible = true;
                IsMySongList = true;
            }

            _loadedPlaylistDetail = true;
        }

        public async Task LoadCurrentPage()
        {
            if (!_loadedPages.Add(CurrentPage))
                return;

            if (!await EnsurePlaylistLoadedAsync("加载歌单歌曲出错"))
            {
                _loadedPages.Remove(CurrentPage);
                return;
            }

            (bool hasMore, List<ProvidableItemBase> items) rst;
            try
            {
                rst = await _progressivePlaylist.GetProgressiveItemsListAsync(CurrentPage * 500, 500);
            }
            catch (Exception ex)
            {
                _loadedPages.Remove(CurrentPage);
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

        private async Task<bool> EnsurePlaylistLoadedAsync(string errorTitle)
        {
            _playlistContainer ??= PlayList;
            _progressivePlaylist ??= _playlistContainer as IProgressiveLoadingContainer;
            if (_progressivePlaylist is not null)
                return true;

            _notification.ShowMessage(errorTitle, "未找到歌单信息");
            return false;
        }

        private static bool IsLikedMusicPlaylist(ContainerBase playlist, IAuthService auth)
        {
            return auth.MySongLists.Count > 0 && playlist.ActualId == auth.MySongLists[0].ActualId;
        }

        public async Task LoadAlbumImage()
        {
            if (CoverUri is null) return;
            using var result = await _httpClient.GetAsync(CoverUri);
            if (result.IsSuccessStatusCode)
            {
                using var stream = await result.Content.ReadAsStreamAsync();
                using var inputStream = stream.AsRandomAccessStream();
                Color imageMainColor = await ColorExtractor.ExtractColorFromStream(inputStream);
                AlbumColor = imageMainColor;
            }
        }

        private void StartAlbumImageLoad()
        {
            var coverUrl = CoverUri?.ToString();
            if (string.Equals(_loadedAlbumImageUrl, coverUrl, StringComparison.Ordinal) &&
                _loadAlbumImageTask is { IsCompleted: false })
                return;

            if (string.Equals(_loadedAlbumImageUrl, coverUrl, StringComparison.Ordinal) &&
                _loadAlbumImageTask is { IsCompletedSuccessfully: true })
                return;

            _loadedAlbumImageUrl = coverUrl;
            _loadAlbumImageTask = LoadAlbumImage();
            _loadAlbumImageTask.SafeFireAndForget();
        }

        public void GreedlyLoad()
        {
            if (HasMore && _greedyLoadTreashold-- <= 0)
            {
                CurrentPage++;
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
            if (!IsDailyRecommend)
            {
                _queueLoader.AppendSourceByKindAsync(SongListQueueScopeKind.Playlist, PlayList.ActualId).SafeFireAndForget();
            }
            else
            {
                _playCore.InsertSongRangeAsync(GetDailyRecommendProviderSongs().ToList()).SafeFireAndForget();
            }
        }
        [RelayCommand]
        private void NavigateToComments()
        {
            _navigation.Navigate(typeof(Comments.Comments), CommentTarget.Playlist(PlayList.ActualId));
        }
        [RelayCommand]
        private async Task ResetCacheAsync()
        {
            try
            {
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracks, PlayList.ActualId);
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracksDetail, PlayList.ActualId, true);
                await SimpleCacher.ResetCacheAsync(CacheType.PlaylistDetail, PlayList.ActualId);
                _notification.ShowMessage("清除缓存成功", "已清除当前歌单的缓存");
                Songs.Clear();
                _dailyRecommendProviderSongs.Clear();
                _loadedPages.Clear();
                CurrentPage = 0;
                _playlistContainer = null;
                _progressivePlaylist = null;
                _loadedPlaylistDetail = false;
                _loadedDailyRecommend = false;
                _loadSongListTask = LoadSongListItem();
                _loadSongListTask.SafeFireAndForget();

            }
            catch
            {
                // ignore
            }
        }

        [RelayCommand]
        private async Task PlayAllAsync()
        {
            if (!IsDailyRecommend)
            {
                await _playCore.StopAsync();
                await _playCore.RemoveAllSongAsync();
                await _navigator.AppendAsync(new MusicResource.Playlist(PlayList.ActualId));
                await _control.MoveNextAndPlayAsync(userInitiated: true);
            }
            else
            {
                await _playCore.StopAsync();
                await _playCore.RemoveAllSongAsync();
                await _playCore.InsertSongRangeAsync(GetDailyRecommendProviderSongs().ToList());
                _navigator.SetPlaybackSource(new MusicResource.DailyRecommend(PlayList.ActualId));
                await _control.MoveNextAndPlayAsync(userInitiated: true);
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
            Api.EnterIntelligencePlay(PlayList.ActualId).SafeFireAndForget();
        }

        [RelayCommand]
        private void DownloadAll()
        {
            if (IsDailyRecommend && HasCompleteDailyRecommendProviderSongs())
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
                if (Subscribed)
                {
                    await _containerItemManagement.RemoveItemFromContainerAsync(PlayList.TypeId, PlayList.ActualId);
                    Subscribed = false;
                }
                else
                {
                    _notification.ShowMessage("暂不支持收藏", "当前抽象只支持从集合中移出项目");
                }
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

        [RelayCommand]
        private void NavigateToAuthor()
        {
            if (!string.IsNullOrWhiteSpace(CreatorId))
                _navigation.Navigate(typeof(Me), CreatorId);
        }

        private async Task<ContainerBase?> LoadProviderPlaylistAsync(string playlistId)
        {
            return await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.PlaylistTypeId + playlistId) as ContainerBase;
        }

        private async Task<List<ProvidableItemBase>> LoadDailyRecommendContainerItemsAsync()
        {
            if (!_specialContainerTypeIds.SpecialContainerTypeIds.TryGetValue(SpecialContainerType.RecommendedSongs, out var typeId))
                return [];

            return await _itemProvider.GetProvidableItemByIdAsync(typeId + "rcsg") is ContainerBase container
                ? await LoadContainerItemsAsync(container)
                : [];
        }

        private async Task LoadCreatorAsync(ContainerBase container)
        {
            var creators = container is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync() : null;
            var creator = creators?.FirstOrDefault();
            CreatorName = creator?.Name ?? string.Empty;
            CreatorId = creator?.ActualId;
        }

        private static async Task<Uri?> GetCoverUriAsync(ContainerBase container)
        {
            if (container is not IHasCover coverProvider)
                return null;

            var cover = await coverProvider.GetCoverAsync();
            return cover is IResourceResultOf<Uri?> uriResult ? await uriResult.GetResourceAsync() : null;
        }
    }
}
