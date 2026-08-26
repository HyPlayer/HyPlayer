using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Playlist;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Lists;

namespace HyPlayer.Features.Home;

public partial class HomeViewModel : ObservableObject
{
#nullable enable
    private readonly IContainerManagementProvidable _containerManager;
    private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier;
    private readonly IAppNavigator _navigator;
    private readonly INotificationService _notification;
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IAuthService _auth;
    private readonly IProviderSpecialContainerTypeIds _specialContainerTypeIds;
    private readonly PlayCoreBase _playCore;
    private readonly IPlaybackControlService _control;
    private readonly INavigationService _navigation;
    private readonly IUserLibraryStateService _userLibraryState;
    private List<SingleSongBase> _recommendedProviderSongs = [];
    private Task? _loadDataTask;

    [ObservableProperty] public partial List<HomeContainerCardViewModel> RecommendedPlaylist { get; set; }

    [ObservableProperty] public partial List<HomeContainerCardViewModel> ToplistPlaylist { get; set; }

    [ObservableProperty] public partial List<ProvidableItemRowViewModel> RecommendedSongItems { get; set; }

    [ObservableProperty] public partial List<HomeContainerCardViewModel> OfficialPlaylists { get; set; }
#nullable restore
    public HomeViewModel(
        IContainerManagementProvidable containerManager,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier,
        IAppNavigator navigator,
        INotificationService notification,
        IProvidableItemProvidable itemProvider,
        IProviderSpecialContainerTypeIds specialContainerTypeIds,
        PlayCoreBase playCore,
        IPlaybackControlService control,
        INavigationService navigation,
        IUserLibraryStateService userLibraryState,
        IAuthService auth)
    {
        _containerManager = containerManager;
        _playlistCollectionChangeNotifier = playlistCollectionChangeNotifier;
        _navigator = navigator;
        _notification = notification;
        _itemProvider = itemProvider;
        _specialContainerTypeIds = specialContainerTypeIds;
        _playCore = playCore;
        _control = control;
        _navigation = navigation;
        _userLibraryState = userLibraryState;
        _auth = auth;
    }

    public Task LoadAsync()
    {
        return _loadDataTask ??= LoadDataCoreAsync();
    }

    public Task RefreshAsync()
    {
        if (_loadDataTask is { IsCompleted: false })
            return _loadDataTask;

        _loadDataTask = LoadDataCoreAsync();
        return _loadDataTask;
    }

    private async Task LoadDataCoreAsync()
    {
        var isLoggedIn = _auth.IsLoggedIn;
        ToplistPlaylist = await LoadSpecialContainerCardsAsync(SpecialContainerType.Toplists, "chart");
        OfficialPlaylists = await LoadSpecialContainerCardsAsync(SpecialContainerType.PlaylistCategory, "官方");
        // 登录内容
        if (isLoggedIn)
        {
            RecommendedPlaylist =
                await LoadSpecialContainerCardsAsync(SpecialContainerType.RecommendedPlaylists, "rcpl");
            _recommendedProviderSongs =
                (await LoadSpecialContainerItemsAsync(SpecialContainerType.RecommendedSongs, "rcsg"))
                .OfType<SingleSongBase>()
                .ToList();
            var displayResolver = ProvidableItemDisplayResolver.CreateDefault();
            RecommendedSongItems =
                (await Task.WhenAll(_recommendedProviderSongs.Select((song, index) =>
                    displayResolver.CreateRowAsync(song, index)))).ToList();
        }
        else
        {
            RecommendedPlaylist = [];
            _recommendedProviderSongs = [];
            RecommendedSongItems = [];
        }

    }

    private async Task<List<HomeContainerCardViewModel>> LoadSpecialContainerCardsAsync(SpecialContainerType type,
        string actualId)
    {
        var items = await LoadSpecialContainerItemsAsync(type, actualId);
        var containers = items.OfType<ContainerBase>().ToList();
        var cards = await Task.WhenAll(containers.Select(CreateContainerCardAsync));
        return cards.ToList();
    }

    private async Task<List<ProvidableItemBase>> LoadSpecialContainerItemsAsync(SpecialContainerType type,
        string actualId)
    {
        if (!_specialContainerTypeIds.SpecialContainerTypeIds.TryGetValue(type, out var typeId))
            return [];

        return await _itemProvider.GetProvidableItemByIdAsync(typeId + actualId) is ContainerBase container
            ? await LoadContainerItemsAsync(container)
            : [];
    }

    private async Task<HomeContainerCardViewModel> CreateContainerCardAsync(ContainerBase container)
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
            Description = container is IHasDescription descriptionProvider
                ? descriptionProvider.Description ?? string.Empty
                : string.Empty,
            DeletePlaylistCommand = DeletePlaylistCommand,
            PublishPlaylistCommand = PublishPlaylistCommand,
            PlayPlaylistCommand = PlayPlaylistCommand
        };
    }

    private static async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(ContainerBase container)
    {
        return container switch
        {
            IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0,
                progressive.MaxProgressiveCount)).Item2,
            LinerContainerBase liner => await liner.GetAllItemsAsync(),
            UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(),
            _ => []
        };
    }

    [RelayCommand]
    private void OnLikedClicked()
    {
        if (_userLibraryState.LikedSongsPlaylist is { ActualId: { Length: > 0 } likedSongs })
            _navigation.Navigate(typeof(SongListDetail), likedSongs);
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
        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await _playCore.InsertSongRangeAsync(_recommendedProviderSongs);
        await _control.MoveNextAndPlayAsync(true);
    }
    [RelayCommand]
    private Task PlayPlaylistAsync(HomeContainerCardViewModel playlist) =>
        _navigator.PlayAsync(new MusicResource.Playlist(playlist.ActualId));

    [RelayCommand]
    private async Task PublishPlaylistAsync(HomeContainerCardViewModel playlist)
    {
        try
        {
            await _containerManager.SetContainerPrivacyAsync(playlist.ActualId, true);
            _notification.ShowMessage("成功公开歌单");
            _playlistCollectionChangeNotifier.NotifyChanged();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("公开歌单失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeletePlaylistAsync(HomeContainerCardViewModel playlist)
    {
        try
        {
            await _containerManager.DeleteContainerAsync(playlist.ActualId);
            _notification.ShowMessage("成功删除");
            _playlistCollectionChangeNotifier.NotifyChanged();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("删除歌单失败", ex.Message);
        }
    }
}

public sealed class HomeContainerCardViewModel
{
    public required ContainerBase Container { get; init; }
    public string? ActualId { get; init; }
    public string? Name { get; init; }
    public string CoverUrl { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ICommand DeletePlaylistCommand { get; init; }
    public ICommand PublishPlaylistCommand { get; init; }
    public ICommand PlayPlaylistCommand { get; init; }
}