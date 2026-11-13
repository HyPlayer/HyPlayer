using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Navigation;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Radio.Services;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Shell.Login;
using ObservableCollections;

namespace HyPlayer.Shell.Navigation;

/// <summary>
///     导航侧边栏 ViewModel — 管理所有导航节点（静态页 + 动态歌单 + 底部项）。
///     替代 PageToNavigationViewIndicatorConverter 的反射方案，NativeAOT 安全。
/// </summary>
public partial class NavigationShellViewModel : ObservableObject
{
    private const string LibrarySectionKey = "library";

    private readonly IAuthService _auth;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly SidebarLayout _layout;
    private readonly NavigationNode _libraryStatusNode;
    private readonly INotificationService _notification;
    private readonly NavigationNode _podcastCollectedNode;
    private readonly NavigationNode _podcastCreatedNode;
    private readonly IPodcastLibraryService _podcastLibrary;
    private readonly NavigationNode _podcastLikedNode;
    private readonly NavigationNode _podcastStatusNode;
    private readonly FontIcon _statusLoadingIcon = new() { Glyph = "\uE895" };
    private readonly FontIcon _statusRetryIcon = new() { Glyph = "\uE72C" };
    private readonly NavigationNode _podcastSubscribedNode;

    private readonly List<NavigationNode> _providerLibraryNodes = [];
    private readonly List<NavigationSection> _sections = [];
    private readonly IUserLibraryNavigationProvidable _userLibraryNavigationProvider;
    private readonly IUserLibraryStateService _userLibraryState;
    private readonly IUserLibraryTypeIds _userLibraryTypeIds;
    private AutoLoginState _autoLoginState;
    private Task? _loadPlaylistsTask;

    public NavigationShellViewModel(
        IAuthService auth,
        INotificationService notification,
        IUserLibraryTypeIds userLibraryTypeIds,
        IUserLibraryNavigationProvidable userLibraryNavigationProvider,
        IUserLibraryStateService userLibraryState,
        IProviderKnownTypeIds knownTypeIds,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier,
        SidebarLayout layout,
        IPodcastLibraryService podcastLibrary,
        ILoadEnvironment loadEnvironment)
    {
        MenuItemsView = MenuItems.ToNotifyCollectionChanged();
        LibraryLoad = new LoadState("歌单", loadEnvironment);
        PodcastLoad = new LoadState("播客", loadEnvironment);
        _libraryStatusNode = new NavigationNode
        {
            Key = "library-status", IsStatus = true, SelectsOnInvoked = false, IsVisible = false,
            Action = AppNavigationAction.ReloadLibrary
        };
        _podcastStatusNode = new NavigationNode
        {
            Key = "podcast-status", IsStatus = true, SelectsOnInvoked = false, IsVisible = false,
            Action = AppNavigationAction.ReloadPodcasts
        };
        LibraryLoad.PropertyChanged += (_, e) =>
        {
            IsLoading = LibraryLoad.IsLoading;
            UpdateStatusNode(_libraryStatusNode, LibraryLoad);
            if (e.PropertyName == nameof(LoadState.Phase) && LibraryLoad is { IsFailed: true, Failure: { } failure })
                _notification.ShowMessage("获取歌单失败", failure.Reason);
        };
        PodcastLoad.PropertyChanged += (_, _) => UpdateStatusNode(_podcastStatusNode, PodcastLoad);
        _auth = auth;
        _notification = notification;
        _userLibraryTypeIds = userLibraryTypeIds;
        _userLibraryNavigationProvider = userLibraryNavigationProvider;
        _userLibraryState = userLibraryState;
        _knownTypeIds = knownTypeIds;
        _layout = layout;
        _layout.Changed += (_, _) => ApplyLayout();
        _podcastLibrary = podcastLibrary;
        _podcastLikedNode = new NavigationNode
        {
            Key = "podcast-liked", Title = "点赞的单集", Icon = new FontIcon { Glyph = "\uE8E1" },
            Route = new AppRoute.PodcastEpisodes(PodcastEpisodeKinds.Liked), IsVisible = false
        };
        _podcastCollectedNode = new NavigationNode
        {
            Key = "podcast-collected", Title = "收藏的单集", Icon = new FontIcon { Glyph = "\uE734" },
            Route = new AppRoute.PodcastEpisodes(PodcastEpisodeKinds.Collected), IsVisible = false
        };
        _podcastCreatedNode = new NavigationNode
        {
            Key = "podcast-created", Title = "我创建的播客", Icon = new FontIcon { Glyph = "\uE720" },
            SelectsOnInvoked = false, IsVisible = false, IsExpanded = true
        };
        _podcastSubscribedNode = new NavigationNode
        {
            Key = "podcast-subscribed", Title = "我收藏的播客", Icon = new FontIcon { Glyph = "\uEC05" },
            SelectsOnInvoked = false, IsVisible = false, IsExpanded = true
        };

        BuildMenuItems();
        UpdateAccountStatus();

        playlistCollectionChangeNotifier.Changed += (_, _) => LoadPlaylistsCommand.Execute(null);
    }

    public ObservableList<NavigationNode> MenuItems { get; } = [];
    public NotifyCollectionChangedSynchronizedViewList<NavigationNode> MenuItemsView { get; }
    public LoadState LibraryLoad { get; }
    public LoadState PodcastLoad { get; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial ImageSource? AccountAvatarSource { get; set; }

    [ObservableProperty] public partial string AccountInitials { get; set; } = "HP";

    [ObservableProperty] public partial string AccountName { get; set; } = "未登录";

    [ObservableProperty] public partial string AccountSubtitle { get; set; } = "登录以同步歌单与收藏";

    [ObservableProperty] public partial string AccountProfileButtonText { get; set; } = "登录";

    [ObservableProperty] public partial Visibility AccountSignOutVisibility { get; set; } = Visibility.Collapsed;

    public AutoLoginState AutoLoginState => _auth.IsLoggedIn ? AutoLoginState.None : _autoLoginState;

    public bool IsAutoLoginPending => AutoLoginState == AutoLoginState.Pending;

    public bool IsAutoLoginUnreachable => AutoLoginState == AutoLoginState.Unreachable;

    public void SetAutoLoginState(AutoLoginState state)
    {
        if (_autoLoginState == state) return;
        _autoLoginState = state;
        _ = UpdateAccountStatusAsync();
    }

    private void BuildMenuItems()
    {
        _sections.AddRange((NavigationSection[])[
            new NavigationSection("discover", "发现",
            (NavigationNode[])[
                new NavigationNode
                {
                    Key = "home", Title = "主页", Icon = new FontIcon { Glyph = "\uE80F" },
                    Route = new AppRoute.Home()
                },
                new NavigationNode
                {
                    Key = "daily", Title = "每日推荐", Icon = new FontIcon { Glyph = "\uE787" },
                    Route = new AppRoute.DailyRecommend()
                },
                new NavigationNode
                {
                    Key = "fm", Title = "私人FM", Icon = new FontIcon { Glyph = "\uF12E" },
                    Action = AppNavigationAction.PersonalFM, SelectsOnInvoked = false
                },
                new NavigationNode
                {
                    Key = "heartbeat", Title = "心动模式", Icon = new FontIcon { Glyph = "\uEB51" },
                    Action = AppNavigationAction.HeartBeat, SelectsOnInvoked = false
                }
            ]),
            new NavigationSection("music", "音乐",
            (NavigationNode[])[
                new NavigationNode
                {
                    Key = "local", Title = "离线音乐", Icon = new FontIcon { Glyph = "\uEC50" },
                    Route = new AppRoute.LocalMusic()
                },
                new NavigationNode
                {
                    Key = "history", Title = "播放历史", Icon = new FontIcon { Glyph = "\uE81C" },
                    Route = new AppRoute.History()
                },
                new NavigationNode
                {
                    Key = "favorite", Title = "我的收藏", Icon = new FontIcon { Glyph = "\uE728" },
                    Route = new AppRoute.Favorite()
                },
                new NavigationNode
                {
                    Key = "cloud", Title = "我的云盘", Icon = new FontIcon { Glyph = "\uE753" },
                    Route = new AppRoute.MusicCloud()
                }
            ]),
            new NavigationSection(LibrarySectionKey, "资料库",
            (NavigationNode[])[
                new NavigationNode
                {
                    Key = "create-playlist", Title = "创建歌单", Icon = new SymbolIcon { Symbol = Symbol.Add },
                    Action = AppNavigationAction.CreatePlaylist, SelectsOnInvoked = false
                },
                _libraryStatusNode
            ]),
            new NavigationSection("podcast", "播客",
            (NavigationNode[])[
                _podcastLikedNode,
                _podcastCollectedNode,
                _podcastCreatedNode,
                _podcastSubscribedNode,
                _podcastStatusNode
            ])
        ]);
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        _layout.Update(_sections
            .Select(section => new SidebarSectionDefinition(
                section.Key,
                section.Header.Title,
                section.Nodes
                    .Where(node => node.Key is not null && node.IsVisible && !node.IsStatus)
                    .GroupBy(node => node.Key!)
                    .Select(group => new SidebarEntryDefinition(group.Key, group.First().Title))
                    .ToList()))
            .Where(section => section.Items.Count > 0)
            .ToList());
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var desired = new List<NavigationNode>();
        foreach (var layoutSection in _layout.Sections)
        {
            if (!layoutSection.IsVisible) continue;

            var section = _sections.FirstOrDefault(candidate => candidate.Key == layoutSection.Key);
            if (section is null) continue;

            var nodes = layoutSection.Items
                .Where(entry => entry.IsVisible)
                .SelectMany(entry => section.Nodes.Where(node => node.Key == entry.Key))
                .ToList();
            if (!nodes.Any(node => node.IsVisible)) continue;

            if (desired.Count > 0)
                desired.Add(section.Separator);
            desired.Add(section.Header);
            desired.AddRange(section.Nodes.Where(node => node.IsStatus));
            desired.AddRange(nodes);
        }

        for (var i = MenuItems.Count - 1; i >= 0; i--)
            if (!desired.Contains(MenuItems[i]))
                MenuItems.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < MenuItems.Count && ReferenceEquals(MenuItems[i], desired[i])) continue;

            var currentIndex = MenuItems.IndexOf(desired[i]);
            if (currentIndex >= 0)
                MenuItems.Move(currentIndex, i);
            else
                MenuItems.Insert(i, desired[i]);
        }
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
        _ = LoadPodcastsAsync();
    }

    public Task RefreshPlaylistsAsync()
    {
        return LoadPlaylistsAsync();
    }

    public Task RefreshPodcastsAsync()
    {
        return _auth.CurrentUser is null || PodcastLoad.IsLoading ? Task.CompletedTask : LoadPodcastsAsync();
    }

    private void UpdateStatusNode(NavigationNode node, LoadState load)
    {
        var visible = load.IsLoading || load.CanRetry;
        if (visible)
        {
            node.Title = load.IsFailed
                ? $"{load.Subject}加载失败，点击重试"
                : load.IsSlow
                    ? $"网络较慢，仍在加载{load.Subject}…"
                    : $"正在加载{load.Subject}…";
            var icon = load.IsFailed ? _statusRetryIcon : _statusLoadingIcon;
            if (!ReferenceEquals(node.Icon, icon))
                node.Icon = icon;
        }

        if (node.IsVisible != visible)
            node.IsVisible = visible;
    }

    public void UpdateAfterLogout()
    {
        ClearProviderLibraryNodes();
        ClearPodcastNodes();
        _userLibraryState.Clear();

        LibraryLoad.Cancel();
        _ = UpdateAccountStatusAsync();
    }

    public void UpdateAccountStatus()
    {
        _ = UpdateAccountStatusAsync();
    }

    private async Task UpdateAccountStatusAsync()
    {
        OnPropertyChanged(nameof(AutoLoginState));
        OnPropertyChanged(nameof(IsAutoLoginPending));
        OnPropertyChanged(nameof(IsAutoLoginUnreachable));
        if (_auth.IsLoggedIn && _auth.CurrentUser is { } user)
        {
            AccountAvatarSource = await TryCreateAvatarSourceAsync(user);
            AccountInitials = GetUserInitials(user.Name);
            AccountName = string.IsNullOrEmpty(user.Name) ? "已登录" : user.Name;
            AccountSubtitle = user is IHasDescription { Description: { Length: > 0 } description }
                ? description
                : "查看个人主页";
            AccountProfileButtonText = "个人资料";
            AccountSignOutVisibility = Visibility.Visible;
            return;
        }

        AccountAvatarSource = null;
        AccountInitials = "HP";
        AccountName = AutoLoginState switch
        {
            AutoLoginState.Pending => "正在登录…",
            AutoLoginState.Unreachable => "自动登录未完成",
            _ => "未登录"
        };
        AccountSubtitle = AutoLoginState switch
        {
            AutoLoginState.Pending => "正在连接至网易云，网络较慢时可能需要一些时间",
            AutoLoginState.Unreachable => "可能是网络较慢或登录已失效，网络恢复后会自动重试",
            _ => "登录以同步歌单与收藏"
        };
        AccountProfileButtonText = AutoLoginState switch
        {
            AutoLoginState.Pending => "正在登录…",
            AutoLoginState.Unreachable => "重试登录",
            _ => "登录"
        };
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
        if (LibraryLoad.IsLoading && _loadPlaylistsTask is not null && !_loadPlaylistsTask.IsCompleted)
            return _loadPlaylistsTask;

        if (_auth.CurrentUser is null)
            return Task.CompletedTask;

        _loadPlaylistsTask = LibraryLoad.RunAsync(
            cancellationToken =>
                _userLibraryNavigationProvider.GetCurrentUserLibraryNavigationGroupsAsync(cancellationToken),
            groups =>
            {
                ClearProviderLibraryNodes();
                _userLibraryState.UpdateFromNavigationGroups(groups);
                RenderProviderLibraryGroups(groups);
                RefreshLayout();
            },
            static groups => groups.Count == 0,
            LoadPlaylistsAsync);
        return _loadPlaylistsTask;
    }

    private Task LoadPodcastsAsync()
    {
        _podcastLikedNode.IsVisible = true;
        _podcastCollectedNode.IsVisible = true;
        RefreshLayout();

        return PodcastLoad.RunAsync(async cancellationToken =>
        {
            var created = await _podcastLibrary.GetCreatedChannelsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            FillPodcastGroup(_podcastCreatedNode, created);

            var subscribed = await LoadRequestProbe.GuardAsync(
                () => _podcastLibrary.GetSubscribedChannelsAsync(cancellationToken),
                static channels => channels.Count == 0);
            cancellationToken.ThrowIfCancellationRequested();
            FillPodcastGroup(_podcastSubscribedNode, subscribed);
            return true;
        });
    }

    private void FillPodcastGroup(NavigationNode group, IReadOnlyList<ContainerBase> channels)
    {
        group.Children.Clear();
        group.Children.AddRange(channels
            .Where(channel => !string.IsNullOrEmpty(channel.ActualId) && !string.IsNullOrEmpty(channel.Name))
            .Select(channel => new NavigationNode
            {
                Title = channel.Name,
                Route = new AppRoute.Radio(channel.ActualId!),
                Icon = new FontIcon { Glyph = "\uE720" }
            }));
        group.IsVisible = group.Children.Count > 0;
        RefreshLayout();
    }

    private void ClearPodcastNodes()
    {
        PodcastLoad.Cancel();
        _podcastLikedNode.IsVisible = false;
        _podcastCollectedNode.IsVisible = false;
        _podcastCreatedNode.IsVisible = false;
        _podcastSubscribedNode.IsVisible = false;
        _podcastCreatedNode.Children.Clear();
        _podcastSubscribedNode.Children.Clear();
        RefreshLayout();
    }

    private void ClearProviderLibraryNodes()
    {
        if (_providerLibraryNodes.Count == 0) return;

        var library = _sections.First(section => section.Key == LibrarySectionKey);
        foreach (var node in _providerLibraryNodes)
            library.Nodes.Remove(node);

        _providerLibraryNodes.Clear();
        RefreshLayout();
    }

    private void RenderProviderLibraryGroups(IReadOnlyList<ProviderLibraryNavigationGroup> groups)
    {
        foreach (var group in groups.OrderBy(group => group.DisplayOrder))
        {
            if (group.IsPinned)
            {
                AddProviderLibraryNodes(group.Items
                    .Select(item => CreateProviderLibraryItemNode(
                        item,
                        group.Id,
                        group.Id == _userLibraryTypeIds.LikedSongsTypeId))
                    .OfType<NavigationNode>());

                continue;
            }

            var groupNode = new NavigationNode
            {
                Key = group.Id,
                Title = group.Title,
                Icon = new SymbolIcon { Symbol = Symbol.List }
            };

            groupNode.Children.AddRange(group.Items
                .Select(item => CreateProviderLibraryItemNode(item, null, false))
                .OfType<NavigationNode>());

            if (groupNode.Children.Count > 0)
                AddProviderLibraryNodes((NavigationNode[])[groupNode]);
        }
    }

    private void AddProviderLibraryNodes(IEnumerable<NavigationNode> nodes)
    {
        var materializedNodes = nodes.ToList();
        _providerLibraryNodes.AddRange(materializedNodes);
        _sections.First(section => section.Key == LibrarySectionKey).Nodes.AddRange(materializedNodes);
    }

    private NavigationNode? CreateProviderLibraryItemNode(ContainerBase item, string? key, bool isLikedSongs)
    {
        if (string.IsNullOrEmpty(item.ActualId) || string.IsNullOrEmpty(item.Name))
            return null;

        var route = isLikedSongs ? new AppRoute.LikedSongs() : TryCreateContainerRoute(item);
        if (route is null)
            return null;

        return new NavigationNode
        {
            Key = key,
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

    private sealed class NavigationSection
    {
        public NavigationSection(string key, string title, IEnumerable<NavigationNode> nodes)
        {
            Key = key;
            Header = new NavigationNode { Title = title, IsHeader = true };
            Nodes.AddRange(nodes);
        }

        public string Key { get; }
        public NavigationNode Header { get; }
        public NavigationNode Separator { get; } = new() { IsSeparator = true };
        public List<NavigationNode> Nodes { get; } = [];
    }
}
