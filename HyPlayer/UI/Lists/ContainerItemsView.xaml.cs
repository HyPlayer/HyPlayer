using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.User;
using HyPlayer.Features.Video;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Dialogs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using ObservableCollections;
using HyPlayer.UI.Lists.IncrementalLoading;
using WinRT;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs;

namespace HyPlayer.UI.Lists;

public sealed partial class ContainerItemsView : UserControl
{
    private const int DefaultPageSize = 500;
    private const string DailyRecommendContainerTypeId = "daily";

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

    public static readonly DependencyProperty QueueScopeProperty = DependencyProperty.Register(
        nameof(QueueScope), typeof(SongListQueueScope), typeof(ContainerItemsView),
        new PropertyMetadata(SongListQueueScope.Visible));

    public static readonly DependencyProperty ExtraItemActionsProperty = DependencyProperty.Register(
        nameof(ExtraItemActions), typeof(IList), typeof(ContainerItemsView), new PropertyMetadata(null));

    public static readonly DependencyProperty ExtraSelectionActionsProperty = DependencyProperty.Register(
        nameof(ExtraSelectionActions), typeof(IList), typeof(ContainerItemsView), new PropertyMetadata(null));

    public static readonly DependencyProperty LoadingTextProperty = DependencyProperty.Register(
        nameof(LoadingText), typeof(string), typeof(ContainerItemsView),
        new PropertyMetadata("正在加载…", (d, _) => ((ContainerItemsView)d).UpdateLoadingState()));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(ContainerItemsView),
        new PropertyMetadata(LoadState.DefaultEmptyText, (d, _) => ((ContainerItemsView)d).UpdateLoadingState()));

    public static readonly DependencyProperty ContainerLoadProperty = DependencyProperty.Register(
        nameof(ContainerLoad), typeof(LoadState), typeof(ContainerItemsView),
        new PropertyMetadata(null, (d, e) => ((ContainerItemsView)d).OnContainerLoadChanged(e.OldValue as LoadState)));

    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly ILoadEnvironment _loadEnvironment = Ioc.Default.GetRequiredService<ILoadEnvironment>();
    private readonly ProvidableItemDisplayResolver _displayResolver = ProvidableItemDisplayResolver.CreateDefault();
    private readonly SwitchingIncrementalSource<ProvidableItemRowViewModel> _rowsSource = new();
    private readonly IncrementalLoadingCollection<SwitchingIncrementalSource<ProvidableItemRowViewModel>, ProvidableItemRowViewModel> _rows;
    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly PlaybackRestriction _restriction = Ioc.Default.GetRequiredService<PlaybackRestriction>();
    private readonly ISongListQueueBuilder _queueBuilder = Ioc.Default.GetRequiredService<ISongListQueueBuilder>();
    private readonly UISettings _setting = Ioc.Default.GetRequiredService<UISettings>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly WeakEventListener<ContainerItemsView, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private bool _isStateSubscribed;
    private bool _isPlayingClickedRow;
    private bool _isReleased;
    private bool _isRowsLoadSuperseded;
    private bool _isViewLoaded;
    private int _moreAttempt;
    private int _rowsAttempt;
    private LoadState? _subscribedContainerLoad;
    private ProvidableItemRowViewModel? _queuedClick;
    private CancellationTokenSource? _eagerLoadCts;

    public ContainerItemsView()
    {
        _rows = new IncrementalLoadingCollection<SwitchingIncrementalSource<ProvidableItemRowViewModel>, ProvidableItemRowViewModel>(
            _rowsSource,
            DefaultPageSize,
            RowsLoadingStarted,
            RowsLoadCompleted,
            RowsLoadFailed);
        State = new ContainerItemsViewState(_rows, _loadEnvironment);
        State.Load.PropertyChanged += Load_PropertyChanged;
        InitializeComponent();
        _stateChangedListener = new WeakEventListener<ContainerItemsView, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) =>
            {
                if (args.PropertyName == nameof(PlaybackStateService.NowPlayingProviderItem))
                    instance._taskRunner.Forget(
                        instance.RunOnUIThreadAsync(() =>
                            instance.UpdateCurrentItem(instance._state.NowPlayingProviderItem)),
                        "update current container item");
            },
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        AttachStateChanged();
        UpdateListHeader(ListHeader);
        State.ActiveItemsSource = State.RowsView;
        SyncLoadState();
    }

    public ContainerItemsViewState State { get; }
    public ObservableList<ProvidableItemRowViewModel> Rows => State.Rows;
    public IReadOnlyCollection<ProvidableItemRowViewModel> VisibleRows => State.VisibleRows;
    public ObservableList<ProvidableItemRowGroup> GroupedItems => State.GroupedItems;

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
        get => (bool)GetValue(CanViewCommentsProperty) && _setting.NotClearMode;
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

    public string LoadingText
    {
        get => (string)GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public LoadState? ContainerLoad
    {
        get => (LoadState?)GetValue(ContainerLoadProperty);
        set => SetValue(ContainerLoadProperty, value);
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

    public IReadOnlyList<SingleSongBase> LoadedProviderSongs =>
        (SingleSongBase[])[.. Rows.Select(row => row.AsPlayableSong)];

    public Task LoadMoreAsync()
    {
        return State.CanRetry ? RetryLoadAsync() : LoadNextPageAsync();
    }

    public async Task PlayAllAsync()
    {
        if (BuildMusicResource(Container) is { } resource)
        {
            await _navigator.PlayAsync(resource);
            return;
        }

        var songs = LoadedProviderSongs.ToList();
        if (songs.Count == 0 || !_restriction.Allows(PlaybackRequest.PlayItems, songs)) return;

        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await _playCore.InsertSongRangeAsync(songs);
        await _control.MoveNextAndPlayAsync(true);
    }

    public async Task AddAllToPlaylistAsync()
    {
        if (BuildMusicResource(Container) is { } resource)
        {
            await _navigator.AppendAsync(resource);
            return;
        }

        var songs = LoadedProviderSongs.ToList();
        if (songs.Count > 0 && _restriction.Allows(PlaybackRequest.PlayItems, songs))
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
        var view = (ContainerItemsView)d;
        if (view._isReleased)
            return;

        if (view.Container != null)
        {
            view.StartLoadForContainer();
            return;
        }

        view.StopEagerLoading();
        view._isRowsLoadSuperseded = view._rows.IsLoading;
        view._rowsSource.Reset(null);
        view._rows.Clear();
        view.GroupedItems.Clear();
        view.State.ActiveItemsSource = view.State.RowsView;
        view.State.Load.Cancel();
        view.State.MoreLoad.Cancel();
    }

    private static void OnListHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ContainerItemsView)d).UpdateListHeader((UIElement?)e.NewValue);
    }

    private static void OnGreedyLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ContainerItemsView)d;
        view.RestartEagerLoading();
    }

    private void Load_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoadState.Phase))
            UpdateLoadingState();
    }

    private void OnContainerLoadChanged(LoadState? previous)
    {
        if (previous is not null && ReferenceEquals(previous, _subscribedContainerLoad))
            DetachContainerLoad();

        if (_isViewLoaded)
            AttachContainerLoad();
        UpdateLoadingState();
    }

    private void AttachContainerLoad()
    {
        if (_isReleased || ReferenceEquals(_subscribedContainerLoad, ContainerLoad))
            return;

        DetachContainerLoad();
        _subscribedContainerLoad = ContainerLoad;
        if (_subscribedContainerLoad is not null)
            _subscribedContainerLoad.PropertyChanged += Load_PropertyChanged;
    }

    private void DetachContainerLoad()
    {
        if (_subscribedContainerLoad is null)
            return;

        _subscribedContainerLoad.PropertyChanged -= Load_PropertyChanged;
        _subscribedContainerLoad = null;
    }

    private void UpdateLoadingState()
    {
        if (LoadingPanel is null || _isReleased) return;
        State.Load.LoadingText = LoadingText;
        State.Load.EmptyText = EmptyText;
        State.ActiveLoad = ContainerLoad is { IsVisible: true } containerLoad ? containerLoad : State.Load;
        PositionLoadingPanel();
    }

    private void PositionLoadingPanel()
    {
        if (LoadingPanel.Visibility != Visibility.Visible || ActualHeight <= 0) return;
        double headerBottom = 0;
        try
        {
            if (HeaderPanel is { ActualHeight: > 0 })
                headerBottom = HeaderPanel.TransformToVisual(this)
                    .TransformPoint(new Point(0, HeaderPanel.ActualHeight)).Y;
        }
        catch
        {
        }

        var top = Math.Clamp(headerBottom, 0, ActualHeight);
        var bottomReserve = Math.Min(130, ActualHeight * 0.15);
        var available = Math.Max(0, ActualHeight - top - bottomReserve);
        var panelHeight = LoadingPanel.ActualHeight > 0 ? LoadingPanel.ActualHeight : 80;
        LoadingPanelOffset.Y = available > panelHeight
            ? top + (available - panelHeight) / 2 - (ActualHeight - panelHeight) / 2
            : 0;
    }

    private void OnLoadingLayoutChanged(object sender, SizeChangedEventArgs e)
    {
        PositionLoadingPanel();
    }

    private void ContainerItemsView_Loaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = true;
        AttachStateChanged();
        State.MultiSelect = false;
        if (HeaderPanel is not null)
        {
            HeaderPanel.SizeChanged -= OnLoadingLayoutChanged;
            HeaderPanel.SizeChanged += OnLoadingLayoutChanged;
        }

        SizeChanged -= OnLoadingLayoutChanged;
        SizeChanged += OnLoadingLayoutChanged;
        LoadingPanel.SizeChanged -= OnLoadingLayoutChanged;
        LoadingPanel.SizeChanged += OnLoadingLayoutChanged;
        AttachContainerLoad();
        UpdateLoadingState();
        if (Rows.Count == 0 && _rows.HasMoreItems && !_rows.IsLoading)
            LoadFirstPageAsync().SafeFireAndForget();
        RestartEagerLoading();
    }

    private void ContainerItemsView_Unloaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = false;
        StopEagerLoading();
        DetachStateChanged();
        DetachContainerLoad();
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
        StopEagerLoading();
        _isRowsLoadSuperseded = _rows.IsLoading;
        var source = ContainerIncrementalPageSource.Create(Container, DefaultPageSize);
        var mappedSource = source is null
            ? null
            : new MappingIncrementalSource<ProvidableItemBase, ProvidableItemRowViewModel>(
                source,
                (item, index, cancellationToken) =>
                    _displayResolver.CreateRowAsync(item, index, cancellationToken));
        _rowsSource.Reset(mappedSource);
        _rows.Clear();
        GroupedItems.Clear();
        State.ActiveItemsSource = State.RowsView;
        QueueScope = BuildQueueScope(Container);
        LoadFirstPageAsync().SafeFireAndForget();
    }

    private async Task LoadFirstPageAsync()
    {
        if (Container is null)
            return;

        await _rows.RefreshAsync();
        RestartEagerLoading();
    }

    private async Task LoadNextPageAsync()
    {
        if (_rows.IsLoading || !_rows.HasMoreItems)
            return;

        await _rows.LoadMoreItemsAsync(DefaultPageSize);
    }

    private async Task RetryLoadAsync()
    {
        if (_isReleased || _rows.IsLoading)
            return;

        var source = ContainerIncrementalPageSource.Create(Container, DefaultPageSize);
        var mappedSource = source is null
            ? null
            : new MappingIncrementalSource<ProvidableItemBase, ProvidableItemRowViewModel>(
                source,
                (item, index, cancellationToken) =>
                    _displayResolver.CreateRowAsync(item, index, cancellationToken));
        _rowsSource.Reset(mappedSource);
        _rows.Clear();
        await _rows.RefreshAsync();
    }

    private void RowsLoadingStarted()
    {
        State.CanRetry = false;
        _isRowsLoadSuperseded = false;
        if (Rows.Count == 0)
        {
            State.MoreLoad.Cancel();
            _rowsAttempt = State.Load.Begin(RetryLoadAsync, out _);
        }
        else
        {
            _moreAttempt = State.MoreLoad.Begin(RetryLoadAsync, out _);
        }

        SyncLoadState();
    }

    private void RowsLoadCompleted()
    {
        RefreshVisibleRows();
        QueueScope = BuildQueueScope(Container);
        SyncLoadState();
        if (_isRowsLoadSuperseded)
            return;

        State.MoreLoad.Complete(_moreAttempt, true);
        if (!State.Load.IsLoading)
            return;

        State.Load.Complete(_rowsAttempt, Rows.Count > 0);
    }

    private void RowsLoadFailed(Exception exception)
    {
        State.CanRetry = true;
        if (Rows.Count == 0)
            State.Load.Fail(_rowsAttempt, exception);
        else
            State.MoreLoad.Fail(_moreAttempt, exception);
    }

    private void SyncLoadState()
    {
        State.IsLoading = _rows.IsLoading;
        State.HasMore = _rows.HasMoreItems;
    }

    private void RefreshVisibleRows()
    {
        var filterText = FilterBox?.Text ?? string.Empty;
        State.ApplyFilter(filterText);

        RebuildGroups();
        UpdateCurrentItem(_state.NowPlayingProviderItem);
    }

    private void RebuildGroups()
    {
        GroupedItems.Clear();
        State.ActiveItemsSource = State.RowsView;
        if (!ShouldGroupByDisc())
            return;

        var grouped = VisibleRows
            .Where(row => !string.IsNullOrWhiteSpace(row.GroupKey))
            .GroupBy(row => row.GroupKey)
            .OrderBy(group => group.Key)
            .ToList();
        if (grouped.Count <= 1)
            return;

        GroupedItems.AddRange(grouped.Select(group =>
            new ProvidableItemRowGroup(group) { Key = group.Key }));

        State.ActiveItemsSource = GroupedItemsViewSource.View;
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

    private void IncrementalLoadSentinel_EffectiveViewportChanged(
        FrameworkElement sender,
        EffectiveViewportChangedEventArgs args)
    {
        if (ReferenceEquals(State.ActiveItemsSource, Rows)
            || !string.IsNullOrWhiteSpace(FilterBox?.Text)
            || args.BringIntoViewDistanceY > sender.ActualHeight
            || !_rows.HasMoreItems
            || _rows.IsLoading)
            return;

        LoadNextPageAsync().SafeFireAndForget();
    }

    private async void ItemList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (!TryGetRow(e.ClickedItem, out var row)) return;
        if (ItemList.SelectionMode == ListViewSelectionMode.Multiple) return;

        if (row.AsPlayableSong is not null)
        {
            if (!row.IsAvailable)
            {
                _notification.ShowMessage("项目不可用", $"{row.Title} 当前不可用");
                return;
            }

            ItemList.SelectedItem = row;
            if (_isPlayingClickedRow)
            {
                _queuedClick = row;
                return;
            }

            await PlayClickedRowAsync(row);
            return;
        }

        if (TryBuildRoute(row) is { } route)
            await _navigator.NavigateAsync(route);
    }

    private async Task PlayClickedRowAsync(ProvidableItemRowViewModel row)
    {
        while (row.AsPlayableSong is { } clickedSong)
        {
            _isPlayingClickedRow = true;
            try
            {
                await _queueBuilder.BuildAndPlayAsync(
                    clickedSong,
                    GetEffectiveQueueScope(),
                    VisibleRows.Select(visibleRow => visibleRow.AsPlayableSong).OfType<SingleSongBase>().ToList());
            }
            catch (Exception ex)
            {
                _notification.ShowFailure($"播放失败：{row.Title}", ex);
            }
            finally
            {
                _isPlayingClickedRow = false;
            }

            if (_queuedClick is not { } queued) return;
            _queuedClick = null;
            if (ReferenceEquals(queued, row)) return;
            row = queued;
        }
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
                foreach (var action in ExtraItemActions.OfType<ProvidableItemAction>())
                {
                    if (action.CanExecute is not null && !action.CanExecute(row))
                        continue;

                    var item = new MenuFlyoutItem
                    {
                        Text = action.Text,
                        Tag = action,
                        Style = (Style)Windows.UI.Xaml.Application.Current.Resources["MenuFlyoutItemRevealStyle"]
                    };
                    item.Click += async (_, _) => await action.ExecuteAsync(row);
                    flyout.Items.Add(item);
                }

            var selectedRows = GetSelectedRows();
            if (ExtraSelectionActions is not null && selectedRows.Count > 0)
                foreach (var action in ExtraSelectionActions.OfType<ProvidableSelectionAction>())
                {
                    if (action.CanExecute is not null && !action.CanExecute(selectedRows))
                        continue;

                    var item = new MenuFlyoutItem
                    {
                        Text = action.Text,
                        Tag = action,
                        Style = (Style)Windows.UI.Xaml.Application.Current.Resources["MenuFlyoutItemRevealStyle"]
                    };
                    item.Click += async (_, _) => await action.ExecuteAsync(selectedRows);
                    flyout.Items.Add(item);
                }
        }
    }

    private static void RemoveInjectedActions(MenuFlyout flyout)
    {
        for (var i = flyout.Items.Count - 1; i >= 0; i--)
            if (flyout.Items[i] is MenuFlyoutItem { Tag: ProvidableItemAction or ProvidableSelectionAction })
                flyout.Items.RemoveAt(i);
    }

    private async void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var row)) return;
        if (row.AsPlayableSong is { } song)
        {
            if (!_restriction.Allows(PlaybackRequest.PlayItems, song)) return;
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
        if (songs.Count == 0 || !_restriction.Allows(PlaybackRequest.PlayItems, songs)) return;

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
            await new ArtistSelectDialog([.. row.Creators]).ShowAsync();
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
            await new SongListSelect(row.ActualId).ShowAsync();
    }

    private void ToolbarNavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
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
        return ResolveEffectiveQueueScope(QueueScope, FilterBox?.Text);
    }

    internal static SongListQueueScope ResolveEffectiveQueueScope(
        SongListQueueScope queueScope,
        string? filterText)
    {
        return string.IsNullOrWhiteSpace(filterText) ? queueScope : SongListQueueScope.Visible;
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
            return SongListQueueScope.Album(container.ActualId);

        if (container.TypeId == _knownTypeIds.PlaylistTypeId)
            return SongListQueueScope.Playlist(container.ActualId);

        if (_knownTypeIds.RadioChannelTypeId is not null && container.TypeId == _knownTypeIds.RadioChannelTypeId)
            return SongListQueueScope.Radio(container.ActualId);

        if (container.TypeId == DailyRecommendContainerTypeId)
            return SongListQueueScope.DailyRecommend(container.ActualId);

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

    private void RestartEagerLoading()
    {
        StopEagerLoading();
        if (!GreedyLoad || !_rows.HasMoreItems || State.CanRetry)
            return;

        _eagerLoadCts = new CancellationTokenSource();
        RunEagerLoadingAsync(_eagerLoadCts.Token).SafeFireAndForget();
    }

    private async Task RunEagerLoadingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (GreedyLoad && _rows.HasMoreItems && !State.CanRetry)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await _rows.LoadMoreItemsAsync(DefaultPageSize);
                if (State.CanRetry)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopEagerLoading()
    {
        _eagerLoadCts?.Cancel();
        _eagerLoadCts?.Dispose();
        _eagerLoadCts = null;
    }

    private void AttachStateChanged()
    {
        if (_isStateSubscribed)
            return;

        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _isStateSubscribed = true;
    }

    private void DetachStateChanged()
    {
        if (!_isStateSubscribed)
            return;

        _stateChangedListener.Detach();
        _isStateSubscribed = false;
    }

    /// <summary>
    /// Releases the XAML and ObservableCollections subscriptions owned by this view.
    /// Call this when the containing page is permanently navigated away from.
    /// </summary>
    public void ReleaseResources()
    {
        if (_isReleased)
            return;

        _isReleased = true;
        StopEagerLoading();
        DetachStateChanged();
        DetachContainerLoad();
        ContainerLoad?.Cancel();
        State.Load.PropertyChanged -= Load_PropertyChanged;
        _rowsSource.Dispose();

        _rows.OnStartLoading = null;
        _rows.OnEndLoading = null;
        _rows.OnError = null;

        _queuedClick = null;
        State.ActiveItemsSource = null;
        ItemList.ItemsSource = null;
        HeaderContentControl.Content = null;
        ListHeader = null;
        Footer = null;
        Container = null;
        ExtraItemActions = null;
        ExtraSelectionActions = null;

        _rows.Clear();
        GroupedItems.Clear();
        State.Dispose();
        Bindings.StopTracking();
    }

    private List<ProvidableItemRowViewModel> GetSelectedRows()
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
            ? (Brush)Windows.UI.Xaml.Application.Current.Resources["DefaultTextForegroundThemeBrush"]
            : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

}
