using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.User;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Shell.Navigation.Services;
using ColorHelper = HyPlayer.Platform.Imaging.ColorHelper;

namespace HyPlayer.Features.Playlist;

public partial class SongListViewModel : ObservableObject, IDisposable
{
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly IContainerItemManagementProvidable _containerItemManagement;
    private readonly ApiSettings _apiSettings;
    private readonly UISettings _uiSettings;
    private readonly INotificationService _notification;
    private readonly INavigationService _navigation;
    private readonly IUserLibraryStateService _userLibraryState;
    private readonly HttpClient _httpClient;

    public SongListViewModel(
        IProvidableItemProvidable itemProvider,
        IProviderKnownTypeIds knownTypeIds,
        IContainerItemManagementProvidable containerItemManagement,
        ApiSettings apiSettings,
        UISettings uiSettings,
        INotificationService notification,
        INavigationService navigation,
        IUserLibraryStateService userLibraryState,
        HttpClient httpClient)
    {
        _itemProvider = itemProvider;
        _knownTypeIds = knownTypeIds;
        _containerItemManagement = containerItemManagement;
        _apiSettings = apiSettings;
        _uiSettings = uiSettings;
        _notification = notification;
        _navigation = navigation;
        _userLibraryState = userLibraryState;
        _httpClient = httpClient;
    }

    [ObservableProperty] public partial ContainerBase PlayList { get; set; }

    [ObservableProperty] public partial bool IsDailyRecommend { get; set; }

    [ObservableProperty] public partial bool IntelligenceModeVisible { get; set; }

    [ObservableProperty] public partial bool IsMySongList { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial bool GreedyLoad { get; set; }

    [ObservableProperty] public partial string DescriptionBoxContent { get; set; }

    [ObservableProperty] public partial string CreatorName { get; set; }

    [ObservableProperty] public partial string? CreatorId { get; set; }

    [ObservableProperty] public partial bool Subscribed { get; set; }
#nullable enable
    [ObservableProperty] public partial string? UpdateTime { get; set; }

    [ObservableProperty] public partial Uri? CoverUri { get; set; }

    [ObservableProperty] public partial Color AlbumColor { get; set; }
#nullable restore
    private CancellationTokenSource? _albumImageCancellationTokenSource;
    private bool _disposed;
    private Task _loadAlbumImageTask;
    private string _loadedAlbumImageUrl;
    private bool _loadedDailyRecommend;
    private bool _loadedPlaylistDetail;
    private string _loadedPlaylistId;

    private ContainerBase _playlistContainer;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAlbumImageLoad();
    }

    public Task LoadPageData(ContainerBase playlist)
    {
        _playlistContainer = playlist;
        PlayList = playlist;
        return LoadPageData(playlist.ActualId);
    }

    public async Task LoadPageData(string PlaylistId, bool loadPlaylist = false)
    {
        var playlistChanged = !string.Equals(_loadedPlaylistId, PlaylistId, StringComparison.Ordinal)
                              || _loadedDailyRecommend != IsDailyRecommend;
        if (playlistChanged)
        {
            _playlistContainer = null;
            CancelAlbumImageLoad();
            _loadedAlbumImageUrl = null;
            _loadedPlaylistDetail = false;
            _loadedDailyRecommend = false;
            IntelligenceModeVisible = false;
            IsMySongList = false;
        }

        _loadedPlaylistId = PlaylistId;

        if (loadPlaylist && (playlistChanged || PlayList?.ActualId != PlaylistId ||
                             _playlistContainer?.ActualId != PlaylistId))
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

        await LoadPlayListItems();
        DescriptionBoxContent = PlayList is IHasDescription descriptionProvider
            ? descriptionProvider.Description ?? string.Empty
            : string.Empty;
        await LoadCreatorAsync(PlayList);
        Subscribed = PlayList is IHasLibraryState { IsInCurrentUserLibrary: true };
        GreedyLoad = _apiSettings.GreedilyLoadPlayContainerItems;
        if (_uiSettings.NoImage)
            CoverUri = null;
        else
            CoverUri = await GetCoverUriAsync(PlayList);
        StartAlbumImageLoad();
        UpdateTime = string.Empty;
        _loadedDailyRecommend = true;
    }

    public async Task LoadPlayListItems()
    {
        if (_loadedPlaylistDetail)
            return;

        _playlistContainer ??= PlayList;
        if (_playlistContainer is null)
        {
            _notification.ShowMessage("加载歌单出错", "未找到歌单信息");
            return;
        }

        PlayList = _playlistContainer;
        if (IsLikedMusicPlaylist(_playlistContainer))
        {
            IntelligenceModeVisible = true;
            IsMySongList = true;
        }

        _loadedPlaylistDetail = true;
    }

    private bool IsLikedMusicPlaylist(ContainerBase playlist)
    {
        return _userLibraryState.IsLikedSongsPlaylist(playlist.ActualId);
    }

    public async Task LoadAlbumImage(CancellationToken cancellationToken)
    {
        if (CoverUri is null) return;
        using var result = await _httpClient.GetAsync(CoverUri, cancellationToken);
        if (result.IsSuccessStatusCode)
        {
            using var stream = await result.Content.ReadAsStreamAsync(cancellationToken);
            using var inputStream = stream.AsRandomAccessStream();
            var imageMainColor = await ColorHelper.ExtractThemeColorFromStream(inputStream);
            cancellationToken.ThrowIfCancellationRequested();
            AlbumColor = imageMainColor;
        }
    }

    private void StartAlbumImageLoad()
    {
        if (_disposed)
            return;

        var coverUrl = CoverUri?.ToString();
        if (string.Equals(_loadedAlbumImageUrl, coverUrl, StringComparison.Ordinal) &&
            _loadAlbumImageTask is { IsCompleted: false })
            return;

        if (string.Equals(_loadedAlbumImageUrl, coverUrl, StringComparison.Ordinal) &&
            _loadAlbumImageTask is { IsCompletedSuccessfully: true })
            return;

        CancelAlbumImageLoad();
        _loadedAlbumImageUrl = coverUrl;
        _albumImageCancellationTokenSource = new CancellationTokenSource();
        _loadAlbumImageTask = LoadAlbumImage(_albumImageCancellationTokenSource.Token);
        _loadAlbumImageTask.SafeFireAndForget();
    }

    private void CancelAlbumImageLoad()
    {
        _albumImageCancellationTokenSource?.Cancel();
        _albumImageCancellationTokenSource?.Dispose();
        _albumImageCancellationTokenSource = null;
        _loadAlbumImageTask = null;
    }

    [RelayCommand]
    private void NavigateToComments()
    {
        _navigation.Navigate(typeof(Comments.Comments), CommentTarget.Playlist(PlayList.ActualId));
    }

    public async Task ResetCacheAsync()
    {
        try
        {
            await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracks, PlayList.ActualId);
            await SimpleCacher.ResetCacheAsync(CacheType.PlaylistTracksDetail, PlayList.ActualId, true);
            await SimpleCacher.ResetCacheAsync(CacheType.PlaylistDetail, PlayList.ActualId);
            _notification.ShowMessage("清除缓存成功", "已清除当前歌单的缓存");
            _playlistContainer = null;
            _loadedPlaylistDetail = false;
            _loadedDailyRecommend = false;
            await LoadPageData(PlayList.ActualId, true);
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private void EnterIntelligencePlay()
    {
        Api.EnterIntelligencePlay(PlayList.ActualId).SafeFireAndForget();
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

    [RelayCommand]
    private void NavigateToAuthor()
    {
        if (!string.IsNullOrWhiteSpace(CreatorId))
            _navigation.Navigate(typeof(Me), CreatorId);
    }

    private async Task<ContainerBase?> LoadProviderPlaylistAsync(string playlistId)
    {
        return await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.PlaylistTypeId + playlistId) as
            ContainerBase;
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
