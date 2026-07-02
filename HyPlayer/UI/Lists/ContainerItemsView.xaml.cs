using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.Playlist;
using HyPlayer.Features.User;
using HyPlayer.Features.Video;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using HyPlayer.Services.Playback;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using WinRT;
using muxc = Microsoft.UI.Xaml.Controls;

namespace HyPlayer.UI.Lists;

public sealed partial class ContainerItemsView : UserControl
{
    private const int DefaultPageSize = 500;
    private const string DailyRecommendContainerTypeId = "daily";

    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly ISongListQueueBuilder _queueBuilder = Ioc.Default.GetRequiredService<ISongListQueueBuilder>();
    private readonly IContainerItemManagementProvidable _containerItemManagement = Ioc.Default.GetRequiredService<IContainerItemManagementProvidable>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly ProvidableItemDisplayResolver _displayResolver = ProvidableItemDisplayResolver.CreateDefault();
    private readonly WeakEventListener<ContainerItemsView, object?, EventArgs> _secondTickListener;
    private readonly WeakEventListener<ContainerItemsView, object?, PropertyChangedEventArgs> _stateChangedListener;
    private CancellationTokenSource? _loadCts;
    private IProgressiveLoadingContainer? _progressiveContainer;
    private UndeterminedContainerBase? _undeterminedContainer;
    private bool _isSecondTickSubscribed;
    private int _greedyLoadThreshold = 3;
    private int _nextOffset;

    public static readonly DependencyProperty ContainerProperty = DependencyProperty.Register(
        nameof(Container), typeof(ContainerBase), typeof(ContainerItemsView),
        new PropertyMetadata(default(ContainerBase), OnContainerChanged));

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        nameof(ListHeader), typeof(UIElement), typeof(ContainerItemsView),
        new PropertyMetadata(default(UIElement), OnListHeaderChanged));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(UIElement), typeof(ContainerItemsView), new PropertyMetadata(default(UIElement)));

    public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
        nameof(IsSearchEnabled), typeof(bool), typeof(ContainerItemsView), new PropertyMetadata(false));

    public static readonly DependencyProperty CanViewCommentsProperty = DependencyProperty.Register(
        nameof(CanViewComments), typeof(bool), typeof(ContainerItemsView), new PropertyMetadata(false));

    public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
        nameof(IsMySongList), typeof(bool), typeof(ContainerItemsView), new PropertyMetadata(false));

    public static readonly DependencyProperty GreedyLoadProperty = DependencyProperty.Register(
        nameof(GreedyLoad), typeof(bool), typeof(ContainerItemsView), new PropertyMetadata(false, OnGreedyLoadChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(ContainerItemsView), new PropertyMetadata(false));

    public static readonly DependencyProperty HasMoreProperty = DependencyProperty.Register(
        nameof(HasMore), typeof(bool), typeof(ContainerItemsView), new PropertyMetadata(false, OnFooterStateChanged));

    public static readonly DependencyProperty QueueScopeProperty = DependencyProperty.Register(
        nameof(QueueScope), typeof(SongListQueueScope), typeof(ContainerItemsView),
        new PropertyMetadata(SongListQueueScope.Visible));

    public static readonly DependencyProperty ExtraItemActionsProperty = DependencyProperty.Register(
        nameof(ExtraItemActions), typeof(IList), typeof(ContainerItemsView), new PropertyMetadata(null));

    public static readonly DependencyProperty ExtraSelectionActionsProperty = DependencyProperty.Register(
        nameof(ExtraSelectionActions), typeof(IList), typeof(ContainerItemsView), new PropertyMetadata(null));

    public ObservableCollection<ProvidableItemRowViewModel> Rows { get; } = [];
    public ObservableCollection<ProvidableItemRowViewModel> VisibleRows { get; } = [];
    public ObservableCollection<ProvidableItemRowGroup> GroupedItems { get; } = [];

    public ContainerItemsView()
    {
        InitializeComponent();
        _secondTickListener = new WeakEventListener<ContainerItemsView, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.GreedyLoadNextPage(),
            OnDetachAction = weakEventListener => { _globalTimer.SecondTick -= weakEventListener.OnEvent; }
        };
        _stateChangedListener = new WeakEventListener<ContainerItemsView, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) =>
            {
                if (args.PropertyName == nameof(PlaybackStateService.NowPlayingProviderItem))
                    instance.UpdateCurrentItem(instance._state.NowPlayingProviderItem);
            },
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        UpdateListHeader(ListHeader);
    }

    public ContainerBase? Container
    {
        get => (ContainerBase?)GetValue(ContainerProperty);
        set => SetValue(ContainerProperty, value);
    }

    public UIElement? ListHeader
    {
        get => (UIElement?)GetValue(ListHeaderProperty);
        set => SetValue(ListHeaderProperty, value);
    }

    public UIElement? Footer
    {
        get => (UIElement?)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool IsSearchEnabled
    {
        get => (bool)GetValue(IsSearchEnabledProperty);
        set => SetValue(IsSearchEnabledProperty, value);
    }

    public bool CanViewComments
    {
        get => (bool)GetValue(CanViewCommentsProperty) && _setting.notClearMode;
        set => SetValue(CanViewCommentsProperty, value);
    }

    public bool IsMySongList
    {
        get => (bool)GetValue(IsMySongListProperty);
        set => SetValue(IsMySongListProperty, value);
    }

    public bool GreedyLoad
    {
        get => (bool)GetValue(GreedyLoadProperty);
        set => SetValue(GreedyLoadProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        private set => SetValue(IsLoadingProperty, value);
    }

    public bool HasMore
    {
        get => (bool)GetValue(HasMoreProperty);
        private set => SetValue(HasMoreProperty, value);
    }

    public SongListQueueScope QueueScope
    {
        get => (SongListQueueScope)GetValue(QueueScopeProperty);
        set => SetValue(QueueScopeProperty, value);
    }

    public IList? ExtraItemActions
    {
        get => (IList?)GetValue(ExtraItemActionsProperty);
        set => SetValue(ExtraItemActionsProperty, value);
    }

    public IList? ExtraSelectionActions
    {
        get => (IList?)GetValue(ExtraSelectionActionsProperty);
        set => SetValue(ExtraSelectionActionsProperty, value);
    }

    public bool MultiSelect { get; set; }
    public bool IsInitialLoading => IsLoading && Rows.Count == 0;
    public bool IsLoadingMore => IsLoading && Rows.Count > 0;
    public bool CanLoadMore => HasMore && !IsLoading;
    public Visibility IsSearchVisible => IsSearchEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CommentsVisible => CanViewComments ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsInitialLoadingVisible => IsInitialLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsLoadingMoreVisible => IsLoadingMore ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CanLoadMoreVisible => CanLoadMore ? Visibility.Visible : Visibility.Collapsed;
    public object ActiveItemsSource => GroupedItems.Count > 0 ? GroupedItemsViewSource.View : VisibleRows;
    public IReadOnlyList<SingleSongBase> LoadedProviderSongs => Rows.Select(row => row.AsPlayableSong).OfType<SingleSongBase>().ToList();

    public void ResetAndLoad()
    {
        StartLoadForContainer();
    }

    public Task LoadMoreAsync()
    {
        return LoadNextPageAsync();
    }

    public async Task PlayAllAsync()
    {
        if (BuildMusicResource(Container) is { } resource)
        {
            await _navigator.PlayAsync(resource);
            return;
        }

        var songs = LoadedProviderSongs.ToList();
        if (songs.Count == 0) return;

        await _playCore.StopAsync();
        await _playCore.RemoveAllSongAsync();
        await _playCore.InsertSongRangeAsync(songs);
        await _control.MoveNextAndPlayAsync(userInitiated: true);
    }

    public async Task AddAllToPlaylistAsync()
    {
        if (BuildMusicResource(Container) is { } resource)
        {
            await _navigator.AppendAsync(resource);
            return;
        }

        var songs = LoadedProviderSongs.ToList();
        if (songs.Count > 0)
            await _playCore.InsertSongRangeAsync(songs);
    }

    public void DownloadAllLoaded()
    {
        var songs = LoadedProviderSongs.ToList();
        if (songs.Count > 0)
            DownloadManager.AddDownload(songs);
    }

    private static void OnContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ContainerItemsView)d).StartLoadForContainer();
    }

    private static void OnListHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ContainerItemsView)d).UpdateListHeader((UIElement?)e.NewValue);
    }

    private static void OnGreedyLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ContainerItemsView)d;
        if ((bool)e.NewValue && view.HasMore)
            view.AttachSecondTick();
        else if (!(bool)e.NewValue)
            view.DetachSecondTick();
    }

    private static void OnFooterStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ContainerItemsView)d;
        view.Bindings.Update();
        if (view.GreedyLoad && view.HasMore)
            view.AttachSecondTick();
        else if (!view.HasMore)
            view.DetachSecondTick();
    }

    private void ContainerItemsView_Loaded(object sender, RoutedEventArgs e)
    {
        MultiSelect = false;
        Bindings.Update();
    }

    private void ContainerItemsView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachSecondTick();
        _stateChangedListener.Detach();
        _loadCts?.Cancel();
    }

    private void UpdateListHeader(UIElement? header)
    {
        if (HeaderPanel is null || HeaderContentControl is null)
            return;

        if (header is not null || IsSearchEnabled)
            HeaderPanel.Padding = new Thickness(0, 0, 0, 25);
        else
            HeaderPanel.Padding = new Thickness(0);

        HeaderContentControl.Content = header;
    }

    private void StartLoadForContainer()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _progressiveContainer = Container as IProgressiveLoadingContainer;
        _undeterminedContainer = Container as UndeterminedContainerBase;
        _nextOffset = 0;
        _greedyLoadThreshold = 3;
        HasMore = false;
        Rows.Clear();
        VisibleRows.Clear();
        GroupedItems.Clear();
        QueueScope = BuildQueueScope(Container);
        LoadFirstPageAsync(_loadCts.Token).SafeFireAndForget();
    }

    private async Task LoadFirstPageAsync(CancellationToken cancellationToken)
    {
        if (Container is null)
            return;

        IsLoading = true;
        Bindings.Update();
        try
        {
            if (_progressiveContainer is not null)
                await LoadProgressivePageAsync(cancellationToken);
            else if (_undeterminedContainer is not null)
                await LoadUndeterminedPageAsync(cancellationToken);
            else if (Container is LinerContainerBase liner)
            {
                var items = await liner.GetAllItemsAsync(cancellationToken);
                await AppendItemsAsync(items, cancellationToken);
                HasMore = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("加载列表失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
            RefreshVisibleRows();
            Bindings.Update();
        }
    }

    private async Task LoadNextPageAsync()
    {
        if (IsLoading || !HasMore)
            return;

        var cancellationToken = _loadCts?.Token ?? CancellationToken.None;
        IsLoading = true;
        Bindings.Update();
        try
        {
            if (_progressiveContainer is not null)
                await LoadProgressivePageAsync(cancellationToken);
            else if (_undeterminedContainer is not null)
                await LoadUndeterminedPageAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("加载更多失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
            RefreshVisibleRows();
            Bindings.Update();
        }
    }

    private async Task LoadProgressivePageAsync(CancellationToken cancellationToken)
    {
        if (_progressiveContainer is null)
            return;

        var pageSize = Math.Clamp(_progressiveContainer.MaxProgressiveCount, 1, DefaultPageSize);
        var (hasMore, items) = await _progressiveContainer.GetProgressiveItemsListAsync(_nextOffset, pageSize, cancellationToken);
        await AppendItemsAsync(items, cancellationToken);
        _nextOffset += items.Count;
        HasMore = hasMore && items.Count > 0;
        QueueScope = BuildQueueScope(Container);
    }

    private async Task LoadUndeterminedPageAsync(CancellationToken cancellationToken)
    {
        if (_undeterminedContainer is null)
            return;

        var items = await _undeterminedContainer.GetNextItemsRangeAsync(cancellationToken);
        await AppendItemsAsync(items, cancellationToken);
        HasMore = items.Count > 0;
        QueueScope = BuildQueueScope(Container);
    }

    private async Task AppendItemsAsync(IEnumerable<ProvidableItemBase> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await _displayResolver.CreateRowAsync(item, Rows.Count, cancellationToken);
            Rows.Add(row);
        }
    }

    private void RefreshVisibleRows()
    {
        var filterText = FilterBox?.Text ?? string.Empty;
        VisibleRows.Clear();
        foreach (var row in Rows.Where(row => row.MatchesFilter(filterText)))
            VisibleRows.Add(row);

        RebuildGroups();
        UpdateCurrentItem(_state.NowPlayingProviderItem);
        Bindings.Update();
    }

    private void RebuildGroups()
    {
        GroupedItems.Clear();
        if (!ShouldGroupByDisc())
            return;

        var grouped = VisibleRows
            .Where(row => !string.IsNullOrWhiteSpace(row.GroupKey))
            .GroupBy(row => row.GroupKey)
            .OrderBy(group => group.Key)
            .ToList();
        if (grouped.Count <= 1)
            return;

        foreach (var group in grouped)
            GroupedItems.Add(new ProvidableItemRowGroup(group) { Key = group.Key });
    }

    private bool ShouldGroupByDisc()
    {
        return Container?.TypeId == _knownTypeIds.AlbumTypeId;
    }

    private void UpdateCurrentItem(SingleSongBase? providerItem)
    {
        foreach (var row in Rows)
            row.IsCurrent = providerItem is not null
                            && row.Item.ProviderId == providerItem.ProviderId
                            && row.Item.TypeId == providerItem.TypeId
                            && row.Item.ActualId == providerItem.ActualId;
    }

    private void FilterBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        RefreshVisibleRows();
    }

    private void LoadMoreButton_Click(object sender, RoutedEventArgs e)
    {
        LoadMoreAsync().SafeFireAndForget();
    }

    private async void ItemList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (!TryGetRow(e.ClickedItem, out var row)) return;
        if (ItemList.SelectionMode == ListViewSelectionMode.Multiple) return;

        if (row.AsPlayableSong is { } clickedSong)
        {
            if (!row.IsAvailable)
            {
                _notification.ShowMessage("项目不可用", $"{row.Title} 当前不可用");
                return;
            }

            await _queueBuilder.BuildAndPlayAsync(
                clickedSong,
                GetEffectiveQueueScope(),
                VisibleRows.Select(visibleRow => visibleRow.AsPlayableSong).OfType<SingleSongBase>().ToList());
            return;
        }

        if (TryBuildRoute(row) is { } route)
            await _navigator.NavigateAsync(route);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
    }

    private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender?.As<Grid>();
        if (element == null) return;

        if (ItemList.SelectionMode == ListViewSelectionMode.Single)
            ItemList.SelectedItem = element.DataContext;

        ItemList.ContextFlyout.ShowAt(element,
            new FlyoutShowOptions { Position = e?.GetPosition(element) ?? new Point(element.ActualWidth, 80) });
    }

    private void ContextFlyout_Opening(object sender, object e)
    {
        if (!TryGetSelectedRow(out var row)) return;

        FlyoutItemPlay.Visibility = CanPlay(row) ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemAddToPlaylist.Visibility = row.AsPlayableSong is not null ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemNavigate.Visibility = TryBuildRoute(row) is not null ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemCreators.Visibility = row.CanOpenCreators ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemAlbum.Visibility = row.Album is not null ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemComments.Visibility = row.CanOpenComments ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemDownload.Visibility = row.CanDownload ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemRichMedia.Visibility = row.CanOpenRichMedia ? Visibility.Visible : Visibility.Collapsed;
        FlyoutItemCollect.Visibility = row.CanCollect ? Visibility.Visible : Visibility.Collapsed;

        if (sender is MenuFlyout flyout)
        {
            RemoveInjectedActions(flyout);
            if (ExtraItemActions is not null)
            {
                foreach (var action in ExtraItemActions.OfType<ProvidableItemAction>())
                {
                    if (action.CanExecute is not null && !action.CanExecute(row))
                        continue;

                    var item = new MenuFlyoutItem
                    {
                        Text = action.Text,
                        Tag = action,
                        Style = (Style)Application.Current.Resources["MenuFlyoutItemRevealStyle"]
                    };
                    item.Click += async (_, _) => await action.ExecuteAsync(row);
                    flyout.Items.Add(item);
                }
            }

            var selectedRows = GetSelectedRows();
            if (ExtraSelectionActions is not null && selectedRows.Count > 0)
            {
                foreach (var action in ExtraSelectionActions.OfType<ProvidableSelectionAction>())
                {
                    if (action.CanExecute is not null && !action.CanExecute(selectedRows))
                        continue;

                    var item = new MenuFlyoutItem
                    {
                        Text = action.Text,
                        Tag = action,
                        Style = (Style)Application.Current.Resources["MenuFlyoutItemRevealStyle"]
                    };
                    item.Click += async (_, _) => await action.ExecuteAsync(selectedRows);
                    flyout.Items.Add(item);
                }
            }
        }
    }

    private static void RemoveInjectedActions(MenuFlyout flyout)
    {
        for (var i = flyout.Items.Count - 1; i >= 0; i--)
        {
            if (flyout.Items[i] is MenuFlyoutItem { Tag: ProvidableItemAction or ProvidableSelectionAction } injected)
            {
                flyout.Items.RemoveAt(i);
            }
        }
    }

    private async void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var row)) return;
        if (row.AsPlayableSong is { } song)
        {
            await _playCore.InsertSongAsync(song);
            await _playCore.MovePointerToAsync(song);
            if (_playCore.CurrentSong is { } currentSong)
                await _control.LoadAndPlayAsync(currentSong, removeCurrentSongs: false);
            return;
        }

        if (TryBuildMusicResource(row) is { } resource)
            await _navigator.PlayAsync(resource);
    }

    private void FlyoutItemAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var songs = GetSelectedRows().Select(row => row.AsPlayableSong).OfType<SingleSongBase>().ToList();
        if (songs.Count == 0) return;

        _ = _playCore.InsertSongRangeAsync(songs, _state.NowPlayingIndex + 1);
        if (_state.ActiveStrategyId == "shn")
            _ = _playCore.ReRandomAsync();
    }

    private async void FlyoutItemNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var row) && TryBuildRoute(row) is { } route)
            await _navigator.NavigateAsync(route);
    }

    private async void FlyoutItemCreators_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var row) || row.Creators.Count == 0) return;
        if (row.Creators.Count > 1)
            await new UI.Dialogs.ArtistSelectDialog(row.Creators.ToList()).ShowAsync();
        else
            _navigation.Navigate(typeof(ArtistPage), row.Creators[0].ActualId);
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var row) && row.Album?.ActualId is { Length: > 0 } albumId)
            _navigation.Navigate(typeof(AlbumPage), albumId);
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var row)) return;
        var target = row.TypeId == _knownTypeIds.PlaylistTypeId
            ? CommentTarget.Playlist(row.ActualId)
            : row.TypeId == _knownTypeIds.RichMediaTypeId
                ? CommentTarget.MV(row.ActualId)
                : CommentTarget.Song(row.ActualId);
        _navigation.Navigate(typeof(Comments), target);
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        var songs = GetSelectedRows().Select(row => row.AsPlayableSong).OfType<SingleSongBase>().ToList();
        if (songs.Count > 0)
            DownloadManager.AddDownload(songs);
    }

    private void FlyoutItemRichMedia_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var row) && !string.IsNullOrWhiteSpace(row.RichMediaId))
            _navigation.Navigate(typeof(MVPage), row.RichMediaId);
    }

    private async void FlyoutItemCollect_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var row))
            await new UI.Dialogs.SongListSelect(row.ActualId).ShowAsync();
    }

    private void ToolbarNavigationView_ItemInvoked(muxc.NavigationView sender, muxc.NavigationViewItemInvokedEventArgs args)
    {
        var item = args.InvokedItemContainer;
        switch (item.Tag)
        {
            case "FocusingCurrent":
                var providerItem = _state.NowPlayingProviderItem;
                if (providerItem is null) return;
                var row = VisibleRows.FirstOrDefault(row =>
                    row.Item.ProviderId == providerItem.ProviderId
                    && row.Item.TypeId == providerItem.TypeId
                    && row.Item.ActualId == providerItem.ActualId);
                if (row is not null)
                    ItemList.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
                break;
            case "Comments":
                if (Container?.ActualId is { Length: > 0 } id)
                    _navigation.Navigate(typeof(Comments), CommentTarget.Playlist(id));
                break;
        }
    }

    private SongListQueueScope GetEffectiveQueueScope()
    {
        return Rows.Count == VisibleRows.Count ? QueueScope : SongListQueueScope.Visible;
    }

    private bool CanPlay(ProvidableItemRowViewModel row)
    {
        return row.AsPlayableSong is not null || TryBuildMusicResource(row) is not null;
    }

    private AppRoute? TryBuildRoute(ProvidableItemRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.ActualId))
            return null;

        if (row.TypeId == _knownTypeIds.SingleSongTypeId)
            return new AppRoute.Song(row.ActualId);
        if (row.TypeId == _knownTypeIds.AlbumTypeId)
            return new AppRoute.Album(row.ActualId);
        if (row.TypeId == _knownTypeIds.ArtistTypeId)
            return new AppRoute.Artist(row.ActualId);
        if (row.TypeId == _knownTypeIds.PlaylistTypeId)
            return new AppRoute.Playlist(row.ActualId);
        if (row.TypeId == _knownTypeIds.UserTypeId)
            return new AppRoute.Me(row.ActualId);
        if (_knownTypeIds.RichMediaTypeId is not null && row.TypeId == _knownTypeIds.RichMediaTypeId)
            return new AppRoute.MV(row.ActualId);
        if (_knownTypeIds.RadioChannelTypeId is not null && row.TypeId == _knownTypeIds.RadioChannelTypeId)
            return new AppRoute.Radio(row.ActualId);

        return null;
    }

    private MusicResource? TryBuildMusicResource(ProvidableItemRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.ActualId))
            return null;

        if (row.TypeId == _knownTypeIds.SingleSongTypeId)
            return new MusicResource.Song(row.ActualId);
        if (row.TypeId == _knownTypeIds.AlbumTypeId)
            return new MusicResource.Album(row.ActualId);
        if (row.TypeId == _knownTypeIds.ArtistTypeId)
            return new MusicResource.Artist(row.ActualId);
        if (row.TypeId == _knownTypeIds.PlaylistTypeId)
            return new MusicResource.Playlist(row.ActualId);
        if (_knownTypeIds.RadioChannelTypeId is not null && row.TypeId == _knownTypeIds.RadioChannelTypeId)
            return new MusicResource.Radio(row.ActualId);

        return null;
    }

    private SongListQueueScope BuildQueueScope(ContainerBase? container)
    {
        if (container is null || string.IsNullOrWhiteSpace(container.ActualId))
            return SongListQueueScope.Visible;

        if (container.TypeId == _knownTypeIds.AlbumTypeId)
            return SongListQueueScope.Album(container.ActualId, !HasMore);

        if (container.TypeId == _knownTypeIds.PlaylistTypeId)
            return SongListQueueScope.Playlist(container.ActualId, !HasMore);

        if (_knownTypeIds.RadioChannelTypeId is not null && container.TypeId == _knownTypeIds.RadioChannelTypeId)
            return SongListQueueScope.Radio(container.ActualId, !HasMore);

        if (container.TypeId == DailyRecommendContainerTypeId)
            return SongListQueueScope.Content;

        return SongListQueueScope.Visible;
    }

    private MusicResource? BuildMusicResource(ContainerBase? container)
    {
        if (container is null || string.IsNullOrWhiteSpace(container.ActualId))
            return null;

        if (container.TypeId == _knownTypeIds.AlbumTypeId)
            return new MusicResource.Album(container.ActualId);
        if (container.TypeId == _knownTypeIds.PlaylistTypeId)
            return new MusicResource.Playlist(container.ActualId);
        if (_knownTypeIds.RadioChannelTypeId is not null && container.TypeId == _knownTypeIds.RadioChannelTypeId)
            return new MusicResource.Radio(container.ActualId);
        if (container.TypeId == DailyRecommendContainerTypeId)
            return new MusicResource.DailyRecommend(container.ActualId);

        return null;
    }

    private void GreedyLoadNextPage()
    {
        if (!GreedyLoad || !HasMore)
        {
            DetachSecondTick();
            return;
        }

        if (_greedyLoadThreshold-- > 0)
            return;

        _greedyLoadThreshold = 3;
        LoadMoreAsync().SafeFireAndForget();
    }

    private void AttachSecondTick()
    {
        if (_isSecondTickSubscribed)
            return;

        _globalTimer.SecondTick += _secondTickListener.OnEvent;
        _isSecondTickSubscribed = true;
    }

    private void DetachSecondTick()
    {
        if (!_isSecondTickSubscribed)
            return;

        _secondTickListener.Detach();
        _isSecondTickSubscribed = false;
    }

    private IReadOnlyList<ProvidableItemRowViewModel> GetSelectedRows()
    {
        var rows = ItemList.SelectedItems
            .Select(item => TryGetRow(item, out var row) ? row : null)
            .OfType<ProvidableItemRowViewModel>()
            .ToList();
        if (rows.Count == 0 && TryGetSelectedRow(out var selectedRow))
            rows.Add(selectedRow);

        return rows;
    }

    private bool TryGetSelectedRow(out ProvidableItemRowViewModel row)
    {
        var result = TryGetRow(ItemList.SelectedItem, out row);
        return result;
    }

    private static bool TryGetRow(object item, out ProvidableItemRowViewModel row)
    {
        if (item is ProvidableItemRowViewModel itemRow)
        {
            row = itemRow;
            return true;
        }

        row = null;
        return false;
    }

    public static Brush GetBrush(bool isAvailable)
    {
        return isAvailable
            ? (Brush)Application.Current.Resources["DefaultTextForegroundThemeBrush"]
            : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }
}
