using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Features.Playlist;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.UI.Lists;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Features.Home
{
    public partial class HomeViewModel : ObservableRecipient
    {
#nullable enable
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
        private readonly PlayCoreBase _playCore;
        private readonly IPlaybackControlService _control;
        private readonly INavigationService _navigation;
        private List<SingleSongBase> _recommendedProviderSongs = [];
        private Task? _loadDataTask;
        private bool _hasLoaded;
        private string _loadedUserId = string.Empty;

        [ObservableProperty]
        public partial List<NeteasePlaylist> RecommendedPlaylist { get; set; }
        [ObservableProperty]
        public partial List<NeteasePlaylist> ToplistPlaylist { get; set; }
        [ObservableProperty]
        public partial List<SongListItemViewModel> RecommendedSongItems { get; set; }
        [ObservableProperty]
        public partial List<NeteasePlaylist> OfficialPlaylists { get; set; }
#nullable restore
        public HomeViewModel(
            global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
            PlayCoreBase playCore,
            IPlaybackControlService control,
            INavigationService navigation)
        {
            _neteaseProvider = neteaseProvider;
            _playCore = playCore;
            _control = control;
            _navigation = navigation;
        }

        public Task GetDataAsync(bool forceRefresh = false)
        {
            var auth = Ioc.Default.GetRequiredService<IAuthService>();
            var userId = auth.IsLoggedIn ? auth.CurrentUser?.ActualId ?? string.Empty : string.Empty;
            if (!forceRefresh && _hasLoaded && _loadedUserId == userId)
                return Task.CompletedTask;

            if (!forceRefresh && _loadDataTask is not null && !_loadDataTask.IsCompleted)
                return _loadDataTask;

            _loadDataTask = LoadDataCoreAsync(auth, userId);
            return _loadDataTask;
        }

        private async Task LoadDataCoreAsync(IAuthService auth, string userId)
        {
            ToplistPlaylist = (await LoadContainerItemsAsync(new NeteaseToplistContainer { ActualId = "chart", Name = "排行榜" }))
                .OfType<NeteasePlaylist>()
                .ToList();
            OfficialPlaylists = (await LoadContainerItemsAsync(new NeteasePlaylistCategoryContainer { ActualId = "官方", Category = "官方", Name = "官方推荐歌单" }))
                .OfType<NeteasePlaylist>()
                .ToList();
            // 登录内容
            if (auth.IsLoggedIn)
            {
                RecommendedPlaylist = (await LoadContainerItemsAsync(new NeteaseRecommendPlaylistContainer { ActualId = "rcpl", Name = "推荐歌单" }))
                    .OfType<NeteasePlaylist>()
                    .ToList();
                _recommendedProviderSongs = (await LoadContainerItemsAsync(new NeteaseRecommendSongContainer { ActualId = "rcsg", Name = "推荐歌曲" }))
                    .OfType<SingleSongBase>()
                    .ToList();
                RecommendedSongItems = (await Task.WhenAll(_recommendedProviderSongs.Select((song, index) => SongListItemViewModel.FromProviderSongAsync(song, index)))).ToList();
            }
            else
            {
                RecommendedPlaylist = [];
                _recommendedProviderSongs = [];
                RecommendedSongItems = [];
            }

            _loadedUserId = userId;
            _hasLoaded = true;
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
        private void OnLikedClicked()
        {
            _navigation.Navigate(typeof(SongListDetail), Ioc.Default.GetRequiredService<IAuthService>().MySongLists[0].ActualId);
        }

        [RelayCommand]
        private void OnHeartBeatModeClicked()
        {
            _ = Api.EnterIntelligencePlay();
        }

        [RelayCommand]
        private void OnPersonalFmClicked()
        {
            PersonalFM.InitPersonalFM();
        }

        [RelayCommand]
        private async Task OnPlayAllRecommendedSongsClickedAsync()
        {
            await _playCore.StopAsync();
            await _playCore.RemoveAllSongAsync();
            await _playCore.InsertSongRangeAsync(_recommendedProviderSongs);
            await _control.MoveNextAndPlayAsync(userInitiated: true);
        }
    }
}
