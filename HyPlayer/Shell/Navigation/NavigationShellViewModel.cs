using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Navigation;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Shell.Navigation;

/// <summary>
/// 导航侧边栏 ViewModel — 管理所有导航节点（静态页 + 动态歌单 + 底部项）。
/// 替代 PageToNavigationViewIndicatorConverter 的反射方案，NativeAOT 安全。
/// </summary>
public partial class NavigationShellViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly INotificationService _notification;
    private readonly IUserLibraryTypeIds _userLibraryTypeIds;
    private readonly IUserLibraryNavigationProvidable _userLibraryNavigationProvider;
    private readonly IUserLibraryStateService _userLibraryState;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private Task? _loadPlaylistsTask;

    public ObservableCollection<NavigationNode> MenuItems { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial ImageSource? AccountAvatarSource { get; set; }

    [ObservableProperty]
    public partial string AccountInitials { get; set; } = "HP";

    [ObservableProperty]
    public partial string AccountName { get; set; } = "未登录";

    [ObservableProperty]
    public partial string AccountSubtitle { get; set; } = "登录以同步歌单与收藏";

    [ObservableProperty]
    public partial string AccountProfileButtonText { get; set; } = "登录";

    [ObservableProperty]
    public partial Visibility AccountSignOutVisibility { get; set; } = Visibility.Collapsed;

    private readonly List<NavigationNode> _providerLibraryNodes = [];

    public NavigationShellViewModel(
        IAuthService auth,
        INotificationService notification,
        IUserLibraryTypeIds userLibraryTypeIds,
        IUserLibraryNavigationProvidable userLibraryNavigationProvider,
        IUserLibraryStateService userLibraryState,
        IProviderKnownTypeIds knownTypeIds,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier)
    {
        _auth = auth;
        _notification = notification;
        _userLibraryTypeIds = userLibraryTypeIds;
        _userLibraryNavigationProvider = userLibraryNavigationProvider;
        _userLibraryState = userLibraryState;
        _knownTypeIds = knownTypeIds;

        BuildMenuItems();
        UpdateAccountStatus();

        playlistCollectionChangeNotifier.Changed += (_, _) => LoadPlaylistsCommand.Execute(null);
    }

    private void BuildMenuItems()
    {
        MenuItems.Add(new NavigationNode { Title = "发现", IsHeader = true });
        MenuItems.Add(new NavigationNode { Title = "主页", Icon = new FontIcon { Glyph = "\uE80F" }, Route = new AppRoute.Home() });
        MenuItems.Add(new NavigationNode { Title = "每日推荐", Icon = new FontIcon { Glyph = "\uE787" }, Route = new AppRoute.DailyRecommend() });
        MenuItems.Add(new NavigationNode { Title = "私人FM", Icon = new FontIcon { Glyph = "\uF12E" }, Action = AppNavigationAction.PersonalFM, SelectsOnInvoked = false });
        MenuItems.Add(new NavigationNode { Title = "心动模式", Icon = new FontIcon { Glyph = "\uEB51" }, Action = AppNavigationAction.HeartBeat, SelectsOnInvoked = false });
        MenuItems.Add(new NavigationNode { IsSeparator = true });
        MenuItems.Add(new NavigationNode { Title = "音乐", IsHeader = true });
        MenuItems.Add(new NavigationNode { Title = "离线音乐", Icon = new FontIcon { Glyph = "\uEC50" }, Route = new AppRoute.LocalMusic() });
        MenuItems.Add(new NavigationNode { Title = "播放历史", Icon = new FontIcon { Glyph = "\uE81C" }, Route = new AppRoute.History() });
        MenuItems.Add(new NavigationNode { Title = "我的收藏", Icon = new FontIcon { Glyph = "\uE728" }, Route = new AppRoute.Favorite() });
        MenuItems.Add(new NavigationNode { Title = "我的云盘", Icon = new FontIcon { Glyph = "\uE753" }, Route = new AppRoute.MusicCloud() });
        MenuItems.Add(new NavigationNode { IsSeparator = true });
        MenuItems.Add(new NavigationNode { Title = "资料库", IsHeader = true });
        MenuItems.Add(new NavigationNode { Title = "创建歌单", Icon = new SymbolIcon { Symbol = Symbol.Add }, Action = AppNavigationAction.CreatePlaylist, SelectsOnInvoked = false });
    }

    /// <summary>根据导航后的页面类型和参数找到对应的 NavigationNode</summary>
    public NavigationNode? FindNode(AppRoute route)
    {
        foreach (var item in MenuItems)
        {
            if (item.Route == route) return item;

            var found = FindInChildren(item, route);
            if (found is not null) return found;
        }

        return null;
    }

    private static NavigationNode? FindInChildren(NavigationNode parent, AppRoute route)
    {
        foreach (var child in parent.Children)
        {
            if (child.Route == route) return child;
            var found = FindInChildren(child, route);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>登录后更新用户相关节点</summary>
    public void UpdateAfterLogin()
    {
        if (_auth.CurrentUser is null) return;

        _ = UpdateAccountStatusAsync();
    }

    public Task RefreshPlaylistsAsync()
    {
        return LoadPlaylistsAsync();
    }

    public void UpdateAfterLogout()
    {
        ClearProviderLibraryNodes();
        _userLibraryState.Clear();

        IsLoading = false;
        _ = UpdateAccountStatusAsync();
    }

    public void UpdateAccountStatus()
    {
        _ = UpdateAccountStatusAsync();
    }

    private async Task UpdateAccountStatusAsync()
    {
        if (_auth.IsLoggedIn && _auth.CurrentUser is { } user)
        {
            AccountAvatarSource = await TryCreateAvatarSourceAsync(user);
            AccountInitials = GetUserInitials(user.Name);
            AccountName = string.IsNullOrEmpty(user.Name) ? "已登录" : user.Name;
            AccountSubtitle = user is IHasDescription { Description: { Length: > 0 } description } ? description : "查看个人主页";
            AccountProfileButtonText = "个人资料";
            AccountSignOutVisibility = Visibility.Visible;
            return;
        }

        AccountAvatarSource = null;
        AccountInitials = "HP";
        AccountName = "未登录";
        AccountSubtitle = "登录以同步歌单与收藏";
        AccountProfileButtonText = "登录";
        AccountSignOutVisibility = Visibility.Collapsed;
    }

    private static async Task<ImageSource?> TryCreateAvatarSourceAsync(ProvidableItemBase user)
    {
        if (user is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync();
        if (result is not IResourceResultOf<Uri?> typedResult || result.ResourceStatus != ResourceStatus.Success)
            return null;

        var uri = await typedResult.GetResourceAsync();
        if (uri is null)
            return null;

        return new BitmapImage(uri);
    }

    private static string GetUserInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "HP";
        return name.Trim()[0].ToString().ToUpperInvariant();
    }

    [RelayCommand]
    private Task LoadPlaylistsAsync()
    {
        if (_loadPlaylistsTask is not null && !_loadPlaylistsTask.IsCompleted)
            return _loadPlaylistsTask;

        _loadPlaylistsTask = LoadPlaylistsCoreAsync();
        return _loadPlaylistsTask;
    }

    private async Task LoadPlaylistsCoreAsync()
    {
        if (_auth.CurrentUser is null) return;

        IsLoading = true;
        try
        {
            var groups = await _userLibraryNavigationProvider.GetCurrentUserLibraryNavigationGroupsAsync();

            ClearProviderLibraryNodes();
            _userLibraryState.UpdateFromNavigationGroups(groups);
            RenderProviderLibraryGroups(groups);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("获取歌单失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ClearProviderLibraryNodes()
    {
        foreach (var node in _providerLibraryNodes)
            MenuItems.Remove(node);

        _providerLibraryNodes.Clear();
    }

    private void RenderProviderLibraryGroups(IReadOnlyList<ProviderLibraryNavigationGroup> groups)
    {
        foreach (var group in groups.OrderBy(group => group.DisplayOrder))
        {
            if (group.IsPinned)
            {
                foreach (var item in group.Items)
                {
                    var node = CreateProviderLibraryItemNode(
                        item,
                        group.Id == _userLibraryTypeIds.LikedSongsTypeId);
                    if (node is not null)
                        AddProviderLibraryNode(node);
                }

                continue;
            }

            var groupNode = new NavigationNode
            {
                Title = group.Title,
                Icon = new SymbolIcon { Symbol = Symbol.List }
            };

            foreach (var item in group.Items)
            {
                var itemNode = CreateProviderLibraryItemNode(item, false);
                if (itemNode is not null)
                    groupNode.Children.Add(itemNode);
            }

            if (groupNode.Children.Count > 0)
                AddProviderLibraryNode(groupNode);
        }
    }

    private void AddProviderLibraryNode(NavigationNode node)
    {
        _providerLibraryNodes.Add(node);
        MenuItems.Add(node);
    }

    private NavigationNode? CreateProviderLibraryItemNode(ContainerBase item, bool isLikedSongs)
    {
        if (string.IsNullOrEmpty(item.ActualId) || string.IsNullOrEmpty(item.Name))
            return null;

        var route = isLikedSongs ? (AppRoute)new AppRoute.LikedSongs() : TryCreateContainerRoute(item);
        if (route is null)
            return null;

        return new NavigationNode
        {
            Title = isLikedSongs ? "我喜欢的音乐" : item.Name,
            Route = route,
            Icon = new FontIcon { Glyph = isLikedSongs ? "\uE006" : "\uE142" }
        };
    }

    private AppRoute? TryCreateContainerRoute(ContainerBase item)
    {
        if (item.TypeId == _knownTypeIds.PlaylistTypeId)
            return new AppRoute.Playlist(item.ActualId);
        if (item.TypeId == _knownTypeIds.AlbumTypeId)
            return new AppRoute.Album(item.ActualId);
        if (item.TypeId == _knownTypeIds.ArtistTypeId)
            return new AppRoute.Artist(item.ActualId);
        if (_knownTypeIds.RadioChannelTypeId is not null && item.TypeId == _knownTypeIds.RadioChannelTypeId)
            return new AppRoute.Radio(item.ActualId);

        return null;
    }

}
