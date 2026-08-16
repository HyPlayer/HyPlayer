using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Home;
using HyPlayer.Features.Library;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Playlist;
using HyPlayer.Features.Radio;
using HyPlayer.Features.Settings;
using HyPlayer.Features.User;
using HyPlayer.Features.Video;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.UI.Dialogs;
using ObservableCollections;
using BitmapIcon = Windows.UI.Xaml.Controls.BitmapIcon;
using FontIcon = Windows.UI.Xaml.Controls.FontIcon;
using Frame = Windows.UI.Xaml.Controls.Frame;
using IconElement = Windows.UI.Xaml.Controls.IconElement;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewItemHeader = Microsoft.UI.Xaml.Controls.NavigationViewItemHeader;
using NavigationViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs;
using NavigationViewItemSeparator = Microsoft.UI.Xaml.Controls.NavigationViewItemSeparator;
using SymbolIcon = Windows.UI.Xaml.Controls.SymbolIcon;

namespace HyPlayer.Shell.Navigation.Services;

public sealed partial class AppNavigator : IAppNavigator
{
    private const string DailyRecommendPlaylistId = "daily_recommend";
    private readonly IAuthService _auth;
    private readonly IPlaybackControlService _control;
    private readonly INavigationService _navigation;
    private readonly Dictionary<object, NavigationNode> _navigationNodes = [];
    private readonly Dictionary<NavigationNode, object> _navigationObjects = [];
    private readonly Dictionary<NavigationNode, NavigationNodeSubscription> _nodeSubscriptions = [];
    private readonly INotificationService _notification;
    private readonly PlayCoreBase _playCore;
    private readonly IPlaybackQueueLoader _queueLoader;
    private readonly IUserLibraryStateService _userLibraryState;
    private NavigationView? _navigationView;
    private Frame? _rootFrame;
    private NavigationShellViewModel? _shellViewModel;

    public AppNavigator(INavigationService navigation,
        PlayCoreBase playCore,
        IPlaybackQueueLoader queueLoader,
        IPlaybackControlService control,
        IAuthService auth,
        IUserLibraryStateService userLibraryState,
        INotificationService notification)
    {
        _navigation = navigation;
        _playCore = playCore;
        _queueLoader = queueLoader;
        _control = control;
        _auth = auth;
        _userLibraryState = userLibraryState;
        _notification = notification;
    }

    public void AttachNavigationView(NavigationView navigationView,
        Frame rootFrame,
        NavigationShellViewModel shellViewModel)
    {
        DetachCurrentNavigationView();

        _navigation.RootFrame = rootFrame;
        _rootFrame = rootFrame;
        _navigationView = navigationView;
        _shellViewModel = shellViewModel;

        shellViewModel.MenuItemsView.CollectionChanged += MenuItems_CollectionChanged;
        navigationView.ItemInvoked += NavigationView_ItemInvoked;
        RenderNavigationView();
    }

    public void DetachNavigationView(NavigationView navigationView)
    {
        if (!ReferenceEquals(_navigationView, navigationView)) return;

        DetachCurrentNavigationView();
    }

    public void NavigateBack()
    {
        _navigation.NavigateBack();
    }

    public void SyncNavigationViewSelection(Type pageType, object? parameter)
    {
        if (_navigationView is null || _shellViewModel is null) return;

        var route = InferRoute(pageType, parameter);
        _navigationView.SelectedItem = route is null
            ? null
            : FindNavigationViewItem(_navigationView.MenuItems, _shellViewModel.FindNode(route));
    }

    public void ToggleNavigationPane()
    {
        if (_navigationView is not null)
            _navigationView.IsPaneOpen = !_navigationView.IsPaneOpen;
    }

    public Task NavigateAsync(AppRoute route)
    {
        return route switch
        {
            AppRoute.Album album => NavigatePage(typeof(AlbumPage), album.Id),
            AppRoute.Artist artist => NavigatePage(typeof(ArtistPage), artist.Id),
            AppRoute.DailyRecommend => NavigatePage(typeof(SongListDetail), CreateDailyRecommendPlaylist()),
            AppRoute.Favorite => NavigatePage(typeof(PageFavorite)),
            AppRoute.History => NavigatePage(typeof(HistoryPage)),
            AppRoute.Home => NavigatePage(typeof(HomePage)),
            AppRoute.LikedSongs => LikedSongsPage(),
            AppRoute.LocalMusic => NavigatePage(typeof(LocalMusicPage)),
            AppRoute.Me me => NavigatePage(typeof(Me), me.UserId),
            AppRoute.MusicCloud => NavigatePage(typeof(MusicCloudPage)),
            AppRoute.MV mv => NavigatePage(typeof(MVPage), mv.Id),
            AppRoute.Playlist playlist => NavigatePage(typeof(SongListDetail), playlist.Id),
            AppRoute.Radio radio => NavigatePage(typeof(RadioPage), radio.Id),
            AppRoute.Settings => NavigatePage(typeof(Settings)),
            AppRoute.Song song => PlaySongAsync(song.Id),
            _ => throw new InvalidOperationException($"Unrecognized route: {route.GetType().Name}")
        };
    }

    public async Task PlaySongAsync(string songId)
    {
        await AppendAndMoveToAsync(new MusicResource.Song(songId));
    }

    public AppRoute? InferRoute(Type pageType, object? parameter)
    {
        if (pageType == typeof(HomePage)) return new AppRoute.Home();
        if (pageType == typeof(LocalMusicPage)) return new AppRoute.LocalMusic();
        if (pageType == typeof(HistoryPage)) return new AppRoute.History();
        if (pageType == typeof(PageFavorite)) return new AppRoute.Favorite();
        if (pageType == typeof(MusicCloudPage)) return new AppRoute.MusicCloud();
        if (pageType == typeof(Settings)) return new AppRoute.Settings();
        if (pageType == typeof(Me)) return new AppRoute.Me();
        if (pageType == typeof(AlbumPage)) return new AppRoute.Album(parameter?.ToString() ?? "");
        if (pageType == typeof(ArtistPage)) return new AppRoute.Artist(parameter?.ToString() ?? "");
        if (pageType == typeof(MVPage)) return new AppRoute.MV(parameter?.ToString() ?? "");
        if (pageType == typeof(RadioPage)) return new AppRoute.Radio(parameter?.ToString() ?? "");
        if (pageType == typeof(SongListDetail))
        {
            var playlistId = parameter switch
            {
                string id => id,
                ContainerBase pl => pl.ActualId,
                _ => null
            };

            if (!string.IsNullOrEmpty(playlistId))
            {
                if (playlistId == DailyRecommendPlaylistId)
                    return new AppRoute.DailyRecommend();
                if (_userLibraryState.IsLikedSongsPlaylist(playlistId))
                    return new AppRoute.LikedSongs();
                return new AppRoute.Playlist(playlistId);
            }
        }

        return null;
    }

    public async Task PlayAsync(MusicResource resource)
    {
        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await AppendAsync(resource);
        await _control.MoveNextAndPlayAsync(true);
    }

    public async Task AppendAsync(MusicResource resource)
    {
        SetPlaybackSource(resource);
        await _queueLoader.AppendNcSourceAsync(_playCore.PlaySourceId);
    }

    public void SetPlaybackSource(MusicResource resource)
    {
        _playCore.PlaySourceId = resource.ToPlaybackSourceKey();
    }

    private void DetachCurrentNavigationView()
    {
        if (_shellViewModel is not null)
            _shellViewModel.MenuItemsView.CollectionChanged -= MenuItems_CollectionChanged;

        if (_navigationView is not null)
        {
            _navigationView.ItemInvoked -= NavigationView_ItemInvoked;
            ClearNavigationObjects(_navigationView.MenuItems);
            _navigationView.SelectedItem = null;
            RemoveAllNavigationObjects(_navigationView.MenuItems);
        }

        ClearNodeTracking();

        if (_rootFrame is not null && ReferenceEquals(_navigation.RootFrame, _rootFrame))
            _navigation.RootFrame = null;

        _navigationView = null;
        _shellViewModel = null;
        _rootFrame = null;
    }

    private void RenderNavigationView()
    {
        if (_navigationView is null || _shellViewModel is null) return;

        ClearNavigationObjects(_navigationView.MenuItems);
        RemoveAllNavigationObjects(_navigationView.MenuItems);
        ClearNodeTracking();
        AddNavigationNodes(_navigationView.MenuItems, _shellViewModel.MenuItems);
    }

    private void MenuItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_navigationView is null || _shellViewModel is null) return;

        RunOnNavigationViewThread(() =>
        {
            if (_navigationView is null || _shellViewModel is null) return;
            ApplyCollectionChanges(_navigationView.MenuItems, _shellViewModel.MenuItems, e);
        });
    }

    private void RunOnNavigationViewThread(Action action)
    {
        if (_navigationView?.Dispatcher?.HasThreadAccess == true)
        {
            action();
            return;
        }

        _ = _navigationView?.Dispatcher?.RunAsync(CoreDispatcherPriority.Normal, () => action());
    }

    private void AddNavigationNodes(IList<object> target, ObservableList<NavigationNode> source)
    {
        foreach (var node in source)
            target.Add(CreateNavigationObject(node));
    }

    private void ApplyCollectionChanges(IList<object> target,
        ObservableList<NavigationNode> source,
        NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (!IsInsertIndexValid(target, e.NewStartingIndex))
                {
                    ResetNavigationNodes(target, source);
                    break;
                }

                InsertNavigationNodes(target, e.NewStartingIndex, e.NewItems);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveNavigationNodes(target, e.OldItems);
                break;
            case NotifyCollectionChangedAction.Replace:
                RemoveNavigationNodes(target, e.OldItems);
                if (!IsInsertIndexValid(target, e.NewStartingIndex))
                {
                    ResetNavigationNodes(target, source);
                    break;
                }

                InsertNavigationNodes(target, e.NewStartingIndex, e.NewItems);
                break;
            case NotifyCollectionChangedAction.Move:
                MoveNavigationNodes(target, e.OldItems, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Reset:
                ResetNavigationNodes(target, source);
                break;
        }
    }

    private static bool IsInsertIndexValid(IList<object> target, int index)
    {
        return index >= 0 && index <= target.Count;
    }

    private void InsertNavigationNodes(IList<object> target, int index, IList? nodes)
    {
        if (nodes is null) return;

        var insertIndex = index < 0 ? target.Count : index;
        foreach (var node in nodes.OfType<NavigationNode>())
            target.Insert(insertIndex++, CreateNavigationObject(node));
    }

    private void RemoveNavigationNodes(IList<object> target, IList? nodes)
    {
        if (nodes is null) return;

        foreach (var node in nodes.OfType<NavigationNode>())
        {
            if (_navigationObjects.TryGetValue(node, out var navigationObject))
            {
                ClearSelectedItemIfNeeded(navigationObject);
                target.Remove(navigationObject);
            }

            UntrackNavigationNode(node);
        }
    }

    private void MoveNavigationNodes(IList<object> target, IList? nodes, int newStartingIndex)
    {
        if (nodes is null) return;

        var insertIndex = newStartingIndex < 0 ? target.Count : newStartingIndex;
        foreach (var node in nodes.OfType<NavigationNode>().ToList())
        {
            if (!_navigationObjects.TryGetValue(node, out var navigationObject)) continue;

            target.Remove(navigationObject);
        }

        foreach (var node in nodes.OfType<NavigationNode>())
        {
            if (!_navigationObjects.TryGetValue(node, out var navigationObject)) continue;

            target.Insert(insertIndex++, navigationObject);
        }
    }

    private void ResetNavigationNodes(IList<object> target, ObservableList<NavigationNode> source)
    {
        ClearSelectedItemIfOwnedBy(target);
        UntrackNavigationObjects(target);
        RemoveAllNavigationObjects(target);
        AddNavigationNodes(target, source);
    }

    private object CreateNavigationObject(NavigationNode node)
    {
        object navigationObject;

        if (node.IsHeader)
        {
            navigationObject = new NavigationViewItemHeader { Content = node.Title };
        }
        else if (node.IsSeparator)
        {
            navigationObject = new NavigationViewItemSeparator();
        }
        else
        {
            var item = new NavigationViewItem { Tag = node };
            UpdateNavigationViewItem(item, node);
            AddNavigationNodes(item.MenuItems, node.Children);
            navigationObject = item;
        }

        _navigationObjects[node] = navigationObject;
        _navigationNodes[navigationObject] = node;
        _nodeSubscriptions[node] =
            new NavigationNodeSubscription(node, UpdateNavigationObject, ApplyChildCollectionChanges);
        return navigationObject;
    }

    private void UpdateNavigationObject(NavigationNode node)
    {
        if (!_navigationObjects.TryGetValue(node, out var navigationObject)) return;

        if (navigationObject is NavigationViewItemHeader header)
            header.Content = node.Title;
        else if (navigationObject is NavigationViewItem item) UpdateNavigationViewItem(item, node);
    }

    private void ApplyChildCollectionChanges(NavigationNode node, NotifyCollectionChangedEventArgs e)
    {
        if (!_navigationObjects.TryGetValue(node, out var navigationObject)) return;
        if (navigationObject is not NavigationViewItem item) return;

        ApplyCollectionChanges(item.MenuItems, node.Children, e);
    }

    private static void UpdateNavigationViewItem(NavigationViewItem item, NavigationNode node)
    {
        item.Content = node.Title;
        item.Icon = CreateIcon(node.Icon);
        item.SelectsOnInvoked = node.SelectsOnInvoked;
        item.Visibility = node.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static IconElement? CreateIcon(IconElement? source)
    {
        return source switch
        {
            FontIcon fontIcon => new FontIcon
            {
                Glyph = fontIcon.Glyph,
                FontFamily = fontIcon.FontFamily,
                FontSize = fontIcon.FontSize,
                FontStyle = fontIcon.FontStyle,
                FontWeight = fontIcon.FontWeight,
                IsTextScaleFactorEnabled = fontIcon.IsTextScaleFactorEnabled,
                MirroredWhenRightToLeft = fontIcon.MirroredWhenRightToLeft
            },
            SymbolIcon symbolIcon => new SymbolIcon { Symbol = symbolIcon.Symbol },
            BitmapIcon bitmapIcon => new BitmapIcon
            {
                UriSource = bitmapIcon.UriSource,
                ShowAsMonochrome = bitmapIcon.ShowAsMonochrome
            },
            _ => null
        };
    }

    private static NavigationViewItem? FindNavigationViewItem(IList<object> items, NavigationNode? targetNode)
    {
        if (targetNode is null) return null;

        foreach (var item in items)
        {
            if (item is not NavigationViewItem navigationItem) continue;

            if (ReferenceEquals(navigationItem.Tag, targetNode))
                return navigationItem;

            var childItem = FindNavigationViewItem(navigationItem.MenuItems, targetNode);
            if (childItem is not null)
                return childItem;
        }

        return null;
    }

    private static void ClearNavigationObjects(IList<object> items)
    {
        foreach (var item in items)
        {
            if (item is not NavigationViewItem navigationItem) continue;

            ClearNavigationObjects(navigationItem.MenuItems);
            navigationItem.Icon = null;
        }
    }

    private void ClearSelectedItemIfNeeded(object navigationObject)
    {
        if (_navigationView is null) return;

        if (ReferenceEquals(_navigationView.SelectedItem, navigationObject) ||
            (navigationObject is NavigationViewItem item &&
             ContainsNavigationObject(item.MenuItems, _navigationView.SelectedItem)))
            _navigationView.SelectedItem = null;
    }

    private void ClearSelectedItemIfOwnedBy(IList<object> items)
    {
        if (_navigationView is null) return;
        if (ContainsNavigationObject(items, _navigationView.SelectedItem))
            _navigationView.SelectedItem = null;
    }

    private static bool ContainsNavigationObject(IList<object> items, object? target)
    {
        if (target is null) return false;

        foreach (var item in items)
        {
            if (ReferenceEquals(item, target))
                return true;

            if (item is NavigationViewItem navigationItem && ContainsNavigationObject(navigationItem.MenuItems, target))
                return true;
        }

        return false;
    }

    private void ClearNodeTracking()
    {
        foreach (var subscription in _nodeSubscriptions.Values)
            subscription.Dispose();
        _nodeSubscriptions.Clear();
        _navigationObjects.Clear();
        _navigationNodes.Clear();
    }

    private void UntrackNavigationNode(NavigationNode node)
    {
        if (_navigationObjects.TryGetValue(node, out var navigationObject) &&
            navigationObject is NavigationViewItem item)
        {
            ClearSelectedItemIfNeeded(item);
            UntrackNavigationChildren(item);
            item.Icon = null;
        }

        if (_nodeSubscriptions.Remove(node, out var subscription))
            subscription.Dispose();
        if (_navigationObjects.Remove(node, out navigationObject))
            _navigationNodes.Remove(navigationObject);
    }

    private void UntrackNavigationObjects(IList<object> items)
    {
        foreach (var item in items.ToList())
            if (_navigationNodes.TryGetValue(item, out var node))
                UntrackNavigationNode(node);
    }

    private static void RemoveAllNavigationObjects(IList<object> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
            items.RemoveAt(i);
    }

    private void UntrackNavigationChildren(NavigationViewItem item)
    {
        foreach (var child in item.MenuItems.ToList())
            if (_navigationNodes.TryGetValue(child, out var childNode))
                UntrackNavigationNode(childNode);

        RemoveAllNavigationObjects(item.MenuItems);
    }

    private async void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not NavigationNode node) return;

        try
        {
            if (node.Route is not null)
                await NavigateAsync(node.Route);
            else if (node.Action is { } action) InvokeAction(action);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("导航失败", ex.Message);
        }
    }

    private static void InvokeAction(AppNavigationAction action)
    {
        switch (action)
        {
            case AppNavigationAction.CreatePlaylist:
                _ = new CreateSonglistDialog().ShowAsync();
                break;
            case AppNavigationAction.PersonalFM:
                PersonalFM.InitPersonalFM();
                break;
            case AppNavigationAction.HeartBeat:
                _ = Api.EnterIntelligencePlay();
                break;
        }
    }

    private Task NavigatePage(Type pageType, object? parameter = null)
    {
        _navigation.Navigate(pageType, parameter);
        return Task.CompletedTask;
    }

    private Task LikedSongsPage()
    {
        if (_userLibraryState.LikedSongsPlaylist is { ActualId: { Length: > 0 } likedSongs })
            _navigation.Navigate(typeof(SongListDetail), likedSongs);
        return Task.CompletedTask;
    }

    private async Task AppendAndMoveToAsync(MusicResource resource)
    {
        var sourceKey = resource.ToPlaybackSourceKey();
        await _queueLoader.AppendNcSourceAsync(sourceKey);
        var item = (await _playCore.GetPlaylistAsync()).FirstOrDefault(t => "ns" + t.ActualId == sourceKey);
        if (item is not null)
        {
            await _playCore.MovePointerToAsync(item);
            await _control.LoadAndPlayAsync(item, removeCurrentSongs: false);
        }
    }

    private static ContainerBase CreateDailyRecommendPlaylist()
    {
        return new DailyRecommendContainer
        {
            ActualId = DailyRecommendPlaylistId,
            Name = "每日歌曲推荐"
        };
    }

    private sealed class DailyRecommendContainer : LinerContainerBase, IHasCover, IHasDescription
    {
        public override string ProviderId => string.Empty;
        public override string TypeId => "daily";

        public Task<ResourceResultBase> GetCoverAsync(ImageResourceQualityTag? qualityTag = null,
            CancellationToken ctk = default)
        {
            return Task.FromResult<ResourceResultBase>(new DailyRecommendImageResult
            {
                ResourceStatus = ResourceStatus.Success,
                ExternalException = null,
                Uri = new Uri("https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg")
            });
        }

        public string? Description { get; set; } = "根据你的口味生成，每天6:00更新";

        public override async Task<List<ProvidableItemBase>> GetAllItemsAsync(CancellationToken ctk = default)
        {
            var specialTypeIds = Ioc.Default.GetRequiredService<IProviderSpecialContainerTypeIds>();
            var itemProvider = Ioc.Default.GetRequiredService<IProvidableItemProvidable>();
            if (!specialTypeIds.SpecialContainerTypeIds.TryGetValue(SpecialContainerType.RecommendedSongs,
                    out var typeId))
                return [];

            return await itemProvider.GetProvidableItemByIdAsync(typeId + "rcsg", ctk) is ContainerBase container
                ? await LoadContainerItemsAsync(container, ctk)
                : [];
        }

        private static async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(ContainerBase container,
            CancellationToken ctk)
        {
            return container switch
            {
                IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0,
                    progressive.MaxProgressiveCount, ctk)).Item2,
                LinerContainerBase liner => await liner.GetAllItemsAsync(ctk),
                UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(ctk),
                _ => []
            };
        }
    }

    private sealed class DailyRecommendImageResult : ResourceResultBase, IResourceResultOf<Uri?>
    {
        public override Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }
        public Uri? Uri { get; init; }

        public Task<Uri?> GetResourceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Uri);
        }
    }

    private sealed partial class NavigationNodeSubscription : IDisposable
    {
        private readonly NavigationNode _node;
        private readonly Action<NavigationNode, NotifyCollectionChangedEventArgs> _updateChildren;
        private readonly Action<NavigationNode> _updateNode;

        public NavigationNodeSubscription(NavigationNode node,
            Action<NavigationNode> updateNode,
            Action<NavigationNode, NotifyCollectionChangedEventArgs> updateChildren)
        {
            _node = node;
            _updateNode = updateNode;
            _updateChildren = updateChildren;
            _node.PropertyChanged += Node_Changed;
            _node.ChildrenView.CollectionChanged += Children_CollectionChanged;
        }

        public void Dispose()
        {
            _node.PropertyChanged -= Node_Changed;
            _node.ChildrenView.CollectionChanged -= Children_CollectionChanged;
        }

        private void Node_Changed(object? sender, EventArgs e)
        {
            _updateNode(_node);
        }

        private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _updateChildren(_node, e);
        }
    }
}
