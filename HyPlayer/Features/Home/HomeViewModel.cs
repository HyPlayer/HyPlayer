using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Features.Playlist;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Features.Home
{
    public partial class HomeViewModel : ObservableRecipient
    {
#nullable enable
        private readonly IProvidableItemProvidable _itemProvider;
        private readonly IProviderSpecialContainerTypeIds _specialContainerTypeIds;
        private readonly PlayCoreBase _playCore;
        private readonly IPlaybackControlService _control;
        private readonly INavigationService _navigation;
        private readonly IUserLibraryStateService _userLibraryState;
        private List<SingleSongBase> _recommendedProviderSongs = [];
        private Task? _loadDataTask;
        private bool _hasLoaded;
        private string _loadedUserId = string.Empty;

        [ObservableProperty]
        public partial List<HomeContainerCardViewModel> RecommendedPlaylist { get; set; }
        [ObservableProperty]
        public partial List<HomeContainerCardViewModel> ToplistPlaylist { get; set; }
        [ObservableProperty]
        public partial List<SongListItemViewModel> RecommendedSongItems { get; set; }
        [ObservableProperty]
        public partial List<HomeContainerCardViewModel> OfficialPlaylists { get; set; }
#nullable restore
        public HomeViewModel(
            IProvidableItemProvidable itemProvider,
            IProviderSpecialContainerTypeIds specialContainerTypeIds,
            PlayCoreBase playCore,
            IPlaybackControlService control,
            INavigationService navigation,
            IUserLibraryStateService userLibraryState)
        {
            _itemProvider = itemProvider;
            _specialContainerTypeIds = specialContainerTypeIds;
            _playCore = playCore;
            _control = control;
            _navigation = navigation;
            _userLibraryState = userLibraryState;
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
            ToplistPlaylist = await LoadSpecialContainerCardsAsync(SpecialContainerType.Toplists, "chart");
            OfficialPlaylists = await LoadSpecialContainerCardsAsync(SpecialContainerType.PlaylistCategory, "官方");
            // 登录内容
            if (auth.IsLoggedIn)
            {
                RecommendedPlaylist = await LoadSpecialContainerCardsAsync(SpecialContainerType.RecommendedPlaylists, "rcpl");
                _recommendedProviderSongs = (await LoadSpecialContainerItemsAsync(SpecialContainerType.RecommendedSongs, "rcsg"))
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

        private async Task<List<HomeContainerCardViewModel>> LoadSpecialContainerCardsAsync(SpecialContainerType type, string actualId)
        {
            var items = await LoadSpecialContainerItemsAsync(type, actualId);
            var containers = items.OfType<ContainerBase>().ToList();
            var cards = await Task.WhenAll(containers.Select(CreateContainerCardAsync));
            return cards.ToList();
        }

        private async Task<List<ProvidableItemBase>> LoadSpecialContainerItemsAsync(SpecialContainerType type, string actualId)
        {
            if (!_specialContainerTypeIds.SpecialContainerTypeIds.TryGetValue(type, out var typeId))
                return [];

            return await _itemProvider.GetProvidableItemByIdAsync(typeId + actualId) is ContainerBase container
                ? await LoadContainerItemsAsync(container)
                : [];
        }

        private static async Task<HomeContainerCardViewModel> CreateContainerCardAsync(ContainerBase container)
        {
            var creators = container is IHasCreators creatorsProvider
                ? await creatorsProvider.GetCreatorsAsync()
                : null;
            var cover = container is IHasCover coverProvider
                ? await coverProvider.GetCoverAsync()
                : null;
            var coverUri = cover is IResourceResultOf<Uri?> uriResult ? await uriResult.GetResourceAsync() : null;

            return new HomeContainerCardViewModel
            {
                Container = container,
                Name = container.Name,
                ActualId = container.ActualId,
                CoverUrl = coverUri?.ToString() ?? string.Empty,
                CreatorName = creators?.FirstOrDefault()?.Name ?? string.Empty,
                Description = container is IHasDescription descriptionProvider ? descriptionProvider.Description ?? string.Empty : string.Empty
            };
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
            if (_userLibraryState.LikedSongsPlaylist is { ActualId: { Length: > 0 } likedSongsId })
                _navigation.Navigate(typeof(SongListDetail), likedSongsId);
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

    public sealed partial class HomeContainerCardViewModel
    {
        public required ContainerBase Container { get; init; }
        public string? ActualId { get; init; }
        public string? Name { get; init; }
        public string CoverUrl { get; init; } = string.Empty;
        public string CreatorName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
