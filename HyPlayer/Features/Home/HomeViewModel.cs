using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Application.Loading;
using HyPlayer.Application.State;
using HyPlayer.Domain;
using HyPlayer.Domain.Navigation;
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
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IProviderSpecialContainerTypeIds _specialContainerTypeIds;
    private readonly PlayCoreBase _playCore;
    private readonly IPlaybackControlService _control;
    private readonly INavigationService _navigation;
    private readonly IAppNavigator _appNavigator;
    private readonly IUserLibraryStateService _userLibraryState;
    private List<SingleSongBase> _recommendedProviderSongs = [];
    private Task? _loadDataTask;

    public LoadState ToplistLoad { get; }
    public LoadState OfficialLoad { get; }
    public LoadState RecommendedPlaylistLoad { get; }
    public LoadState RecommendedSongsLoad { get; }

    [ObservableProperty] public partial List<HomeContainerCardViewModel> RecommendedPlaylist { get; set; }

    [ObservableProperty] public partial List<HomeContainerCardViewModel> ToplistPlaylist { get; set; }

    [ObservableProperty] public partial List<ProvidableItemRowViewModel> RecommendedSongItems { get; set; }

    [ObservableProperty] public partial List<HomeContainerCardViewModel> OfficialPlaylists { get; set; }
#nullable restore
    public HomeViewModel(
        IProvidableItemProvidable itemProvider,
        IProviderSpecialContainerTypeIds specialContainerTypeIds,
        PlayCoreBase playCore,
        IPlaybackControlService control,
        INavigationService navigation,
        IAppNavigator appNavigator,
        IUserLibraryStateService userLibraryState,
        ILoadEnvironment loadEnvironment)
    {
        ToplistLoad = new LoadState("排行榜", loadEnvironment) { EmptyText = "暂时没有排行榜" };
        OfficialLoad = new LoadState("官方歌单", loadEnvironment) { EmptyText = "暂时没有官方歌单" };
        RecommendedPlaylistLoad = new LoadState("推荐歌单", loadEnvironment)
        {
            EmptyText = "暂时没有推荐歌单",
            RequiresAccount = true
        };
        RecommendedSongsLoad = new LoadState("推荐单曲", loadEnvironment)
        {
            EmptyText = "暂时没有推荐单曲",
            RequiresAccount = true
        };
        _itemProvider = itemProvider;
        _specialContainerTypeIds = specialContainerTypeIds;
        _playCore = playCore;
        _control = control;
        _navigation = navigation;
        _appNavigator = appNavigator;
        _userLibraryState = userLibraryState;
    }

    public Task GetDataAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _loadDataTask is not null)
            return _loadDataTask;

        if (forceRefresh)
        {
            ToplistPlaylist = [];
            OfficialPlaylists = [];
            RecommendedPlaylist = [];
            RecommendedSongItems = [];
            _recommendedProviderSongs = [];
        }

        _loadDataTask = LoadDataCore();
        return _loadDataTask;
    }

    public void Cancel()
    {
        _loadDataTask = null;
        ToplistLoad.Cancel();
        OfficialLoad.Cancel();
        RecommendedPlaylistLoad.Cancel();
        RecommendedSongsLoad.Cancel();
    }

    private Task LoadDataCore()
    {
        var toplist = ToplistLoad.RunAsync(
            cancellationToken =>
                LoadSpecialContainerCardsAsync(SpecialContainerType.Toplists, "chart", cancellationToken),
            cards => ToplistPlaylist = cards,
            static cards => cards.Count == 0);
        var official = OfficialLoad.RunAsync(
            async cancellationToken =>
            {
                await toplist.WaitAsync(cancellationToken);
                return await LoadSpecialContainerCardsAsync(SpecialContainerType.PlaylistCategory, "官方",
                    cancellationToken);
            },
            cards => OfficialPlaylists = cards,
            static cards => cards.Count == 0);
        var recommended = RecommendedPlaylistLoad.RunAsync(
            async cancellationToken =>
            {
                await official.WaitAsync(cancellationToken);
                return await LoadSpecialContainerCardsAsync(SpecialContainerType.RecommendedPlaylists, "rcpl",
                    cancellationToken);
            },
            cards => RecommendedPlaylist = cards,
            static cards => cards.Count == 0);
        var songs = RecommendedSongsLoad.RunAsync(
            async cancellationToken =>
            {
                await recommended.WaitAsync(cancellationToken);
                return await LoadRecommendedSongsAsync(cancellationToken);
            },
            result =>
            {
                _recommendedProviderSongs = result.Songs;
                RecommendedSongItems = result.Rows;
            },
            static result => result.Rows.Count == 0);
        return Task.WhenAll(toplist, official, recommended, songs);
    }

    private async Task<RecommendedSongs> LoadRecommendedSongsAsync(CancellationToken cancellationToken)
    {
        var songs = (await LoadSpecialContainerItemsAsync(SpecialContainerType.RecommendedSongs, "rcsg",
                cancellationToken))
            .OfType<SingleSongBase>()
            .ToList();
        var displayResolver = ProvidableItemDisplayResolver.CreateDefault();
        var rows = (await Task.WhenAll(songs.Select((song, index) =>
            displayResolver.CreateRowAsync(song, index, cancellationToken)))).ToList();
        return new RecommendedSongs(songs, rows);
    }

    private async Task<List<HomeContainerCardViewModel>> LoadSpecialContainerCardsAsync(SpecialContainerType type,
        string actualId, CancellationToken cancellationToken)
    {
        var items = await LoadSpecialContainerItemsAsync(type, actualId, cancellationToken);
        var containers = items.OfType<ContainerBase>().ToList();
        var cards = await Task.WhenAll(containers.Select(CreateContainerCardAsync));
        return cards.ToList();
    }

    private async Task<List<ProvidableItemBase>> LoadSpecialContainerItemsAsync(SpecialContainerType type,
        string actualId, CancellationToken cancellationToken)
    {
        if (!_specialContainerTypeIds.SpecialContainerTypeIds.TryGetValue(type, out var typeId))
            return [];

        return await _itemProvider.GetProvidableItemByIdAsync(typeId + actualId, cancellationToken) is ContainerBase
            container
            ? await LoadContainerItemsAsync(container, cancellationToken)
            : [];
    }

    private static string GetCardCoverUrl(Uri? coverUri)
    {
        if (coverUri is null) return string.Empty;
        var url = coverUri.ToString();
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !url.Contains('?')
            ? url + "?param=" + StaticSource.PicSizeHomeCardCover
            : url;
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
            CoverUrl = GetCardCoverUrl(coverUri),
            CreatorName = creators?.FirstOrDefault()?.Name ?? string.Empty,
            Description = container is IHasDescription descriptionProvider
                ? descriptionProvider.Description ?? string.Empty
                : string.Empty
        };
    }

    private static async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(ContainerBase container,
        CancellationToken cancellationToken)
    {
        return container switch
        {
            IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0,
                progressive.MaxProgressiveCount, cancellationToken)).Item2,
            LinerContainerBase liner => await liner.GetAllItemsAsync(cancellationToken),
            UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(cancellationToken),
            _ => []
        };
    }

    private sealed record RecommendedSongs(List<SingleSongBase> Songs, List<ProvidableItemRowViewModel> Rows);

    [RelayCommand]
    private void OnLikedClicked()
    {
        if (_userLibraryState.LikedSongsPlaylist is { ActualId: { Length: > 0 } likedSongs })
            _navigation.Navigate(typeof(SongListDetail), likedSongs);
    }

    [RelayCommand]
    private Task OnDailyRecommendClickedAsync()
    {
        return _appNavigator.NavigateAsync(new AppRoute.DailyRecommend());
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
}

public sealed class HomeContainerCardViewModel
{
    public required ContainerBase Container { get; init; }
    public string? ActualId { get; init; }
    public string? Name { get; init; }
    public string CoverUrl { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public override string ToString()
    {
        return Name ?? string.Empty;
    }
}