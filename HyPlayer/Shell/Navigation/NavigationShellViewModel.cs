using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Navigation;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Services.Abstractions;
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
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IAuthService _auth;
    private readonly INotificationService _notification;

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

    // 跟踪需要动态更新的节点
    private NavigationNode? _createdContainer;
    private NavigationNode? _subscribedContainer;
    private NavigationNode? _likedSongsNode;

    public NavigationShellViewModel(
        IProvidableItemProvidable itemProvider,
        IAuthService auth,
        INotificationService notification,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier)
    {
        _itemProvider = itemProvider;
        _auth = auth;
        _notification = notification;

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
        MenuItems.Add(new NavigationNode { Title = "歌单", IsHeader = true });
        MenuItems.Add(new NavigationNode { Title = "创建歌单", Icon = new SymbolIcon { Symbol = Symbol.Add }, Action = AppNavigationAction.CreatePlaylist, SelectsOnInvoked = false });

        _likedSongsNode = new NavigationNode { Title = "我喜欢的音乐", Icon = new FontIcon { Glyph = "\uE006" }, Route = new AppRoute.LikedSongs(), IsVisible = false };
        MenuItems.Add(_likedSongsNode);

        _createdContainer = new NavigationNode { Title = "我创建的歌单", Icon = new SymbolIcon { Symbol = Symbol.List }, IsVisible = false };
        MenuItems.Add(_createdContainer);

        _subscribedContainer = new NavigationNode { Title = "我收藏的歌单", Icon = new SymbolIcon { Symbol = Symbol.List }, IsVisible = false };
        MenuItems.Add(_subscribedContainer);

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

        UpdateAccountStatus();
    }

    public Task RefreshPlaylistsAsync()
    {
        return LoadPlaylistsAsync();
    }

    public void UpdateAfterLogout()
    {
        _createdContainer?.Children.Clear();
        _subscribedContainer?.Children.Clear();

        if (_createdContainer is not null)
            _createdContainer.IsVisible = false;
        if (_subscribedContainer is not null)
            _subscribedContainer.IsVisible = false;
        if (_likedSongsNode is not null)
            _likedSongsNode.IsVisible = false;

        IsLoading = false;
        UpdateAccountStatus();
    }

    public void UpdateAccountStatus()
    {
        if (_auth.IsLoggedIn && _auth.CurrentUser is { } user)
        {
            AccountAvatarSource = string.IsNullOrEmpty(user.AvatarUrl)
                ? null
                : new BitmapImage(new Uri(user.AvatarUrl + "?param=" + StaticSource.PICSIZE_NAVITEM_USERAVATAR));
            AccountInitials = GetUserInitials(user.Name);
            AccountName = string.IsNullOrEmpty(user.Name) ? "已登录" : user.Name;
            AccountSubtitle = string.IsNullOrEmpty(user.Description) ? "查看个人主页" : user.Description;
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

    private static string GetUserInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "HP";
        return name.Trim()[0].ToString().ToUpperInvariant();
    }

    [RelayCommand]
    private async Task LoadPlaylistsAsync()
    {
        if (_auth.CurrentUser is null) return;

        IsLoading = true;
        try
        {
            var user = await _itemProvider.GetProvidableItemByIdAsync(HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.User + _auth.CurrentUser.ActualId);
            var containers = user is ContainersContainer containersContainer
                ? await containersContainer.GetSubContainerAsync()
                : [];

            _createdContainer?.Children.Clear();
            _subscribedContainer?.Children.Clear();
            _auth.MySongLists.Clear();

            var playlistContainers = containers.OfType<NeteaseUserPlaylistSubContainer>().ToList();
            var createdPlaylists = playlistContainers
                .Where(container => container.Name.Contains("创建", StringComparison.Ordinal))
                .SelectMany(container => container.Playlists)
                .ToList();
            var subscribedPlaylists = playlistContainers
                .Where(container => container.Name.Contains("收藏", StringComparison.Ordinal))
                .SelectMany(container => container.Playlists)
                .ToList();

            // 兼容旧 Provider 直接返回歌单的形态。
            if (createdPlaylists.Count == 0 && subscribedPlaylists.Count == 0)
            {
                createdPlaylists = containers.OfType<NeteasePlaylist>().Where(playlist => !playlist.Subscribed).ToList();
                subscribedPlaylists = containers.OfType<NeteasePlaylist>().Where(playlist => playlist.Subscribed).ToList();
            }

            var playlists = createdPlaylists.Concat(subscribedPlaylists).ToList();

            if (playlists.Count == 0)
            {
                if (_likedSongsNode is not null)
                    _likedSongsNode.IsVisible = false;
                return;
            }

            // 第一个歌单是"我喜欢的音乐"
            if (createdPlaylists.Count > 0)
                _auth.MySongLists.Add(createdPlaylists[0]);

            foreach (var pl in createdPlaylists.Skip(1))
            {
                if (string.IsNullOrEmpty(pl.ActualId) || string.IsNullOrEmpty(pl.Name))
                    continue;

                _auth.MySongLists.Add(pl);
                _createdContainer?.Children.Add(new NavigationNode
                {
                    Title = pl.Name,
                    Route = new AppRoute.Playlist(pl.ActualId),
                    Icon = new FontIcon { Glyph = "\uE142" }
                });
            }

            foreach (var pl in subscribedPlaylists)
            {
                if (string.IsNullOrEmpty(pl.ActualId) || string.IsNullOrEmpty(pl.Name))
                    continue;

                _subscribedContainer?.Children.Add(new NavigationNode
                {
                    Title = pl.Name,
                    Route = new AppRoute.Playlist(pl.ActualId),
                    Icon = new FontIcon { Glyph = "\uE142" }
                });
            }

            if (_createdContainer is not null)
                _createdContainer.IsVisible = _createdContainer.Children.Count > 0;
            if (_subscribedContainer is not null)
                _subscribedContainer.IsVisible = _subscribedContainer.Children.Count > 0;
            if (_likedSongsNode is not null)
                _likedSongsNode.IsVisible = playlists.Count > 0;
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
}
