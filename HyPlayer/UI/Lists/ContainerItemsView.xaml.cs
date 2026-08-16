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
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
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
using HyPlayer.Features.Video;
using ObservableCollections;
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

    private readonly IContainerItemManagementProvidable _containerItemManagement =
        Ioc.Default.GetRequiredService<IContainerItemManagementProvidable>();

    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly ProvidableItemDisplayResolver _displayResolver = ProvidableItemDisplayResolver.CreateDefault();
    private readonly IncrementalLoadController<ProvidableItemRowViewModel> _loadController = new();
    private readonly IncrementalLoadingCollection<ProvidableItemRowViewModel> _rows;
    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly ISongListQueueBuilder _queueBuilder = Ioc.Default.GetRequiredService<ISongListQueueBuilder>();
    private readonly UISettings _setting = Ioc.Default.GetRequiredService<UISettings>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly WeakEventListener<ContainerItemsView, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private bool _isStateSubscribed;
    private CancellationTokenSource? _eagerLoadCts;

    public ContainerItemsView()
    {
        _rows = new IncrementalLoadingCollection<ProvidableItemRowViewModel>(
            _loadController,
            static row => string.IsNullOrWhiteSpace(row.ActualId)
                ? null
                : $"{row.Item.ProviderId}\u001f{row.TypeId}\u001f{row.ActualId}");
        State = new ContainerItemsViewState(_rows);
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
        _loadController.PropertyChanged += LoadController_PropertyChanged;
        _rows.LoadCompleted += Rows_LoadCompleted;
        _rows.LoadFailed += Rows_LoadFailed;
        AttachStateChanged();
        UpdateListHeader(ListHeader);
        State.ActiveItemsSource = State.RowsView;
        SyncLoadState();
    }

    public ContainerItemsViewState State { get; }
    public ObservableList<ProvidableItemRowViewModel> Rows => State.Rows;
    public ObservableList<ProvidableItemRowViewModel> VisibleRows => State.VisibleRows;
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

    private bool IsLoading
    {
        get => State.IsLoading;
        set => State.IsLoading = value;
    }

    private bool HasMore
    {
        get => State.HasMore;
        set => State.HasMore = value;
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

    public void ResetAndLoad()
    {
        StartLoadForContainer();
    }

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
        if (songs.Count == 0) return;

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
        view.RestartEagerLoading();
    }

    private void ContainerItemsView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachStateChanged();
        State.MultiSelect = false;
        if (Rows.Count == 0 && _loadController.HasMore && !_loadController.IsLoading)
            LoadFirstPageAsync().SafeFireAndForget();
        RestartEagerLoading();
    }

    private void ContainerItemsView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopEagerLoading();
        DetachStateChanged();
        _loadController.CancelPending();
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
        var source = ContainerIncrementalPageSource.Create(Container, DefaultPageSize);
        var mappedSource = source is null
            ? null
            : new MappingIncrementalPageSource<ProvidableItemBase, ProvidableItemRowViewModel>(
                source,
                (item, index, cancellationToken) =>
                    _displayResolver.CreateRowAsync(item, index, cancellationToken));
        _rows.Reset(mappedSource);
        VisibleRows.Clear();
        GroupedItems.Clear();
        State.ActiveItemsSource = State.RowsView;
        QueueScope = BuildQueueScope(Container);
        SyncLoadState();
        LoadFirstPageAsync().SafeFireAndForget();
    }

    private async Task LoadFirstPageAsync()
    {
        if (Container is null || !_loadController.HasMore)
            return;

        await _rows.LoadInitialAsync(DefaultPageSize);
        RestartEagerLoading();
    }

    private async Task LoadNextPageAsync()
    {
        if (_loadController.IsLoading || !_loadController.CanAutoLoad)
            return;

        await _rows.LoadInitialAsync(DefaultPageSize);
    }

    private async Task RetryLoadAsync()
    {
        await _rows.RetryAsync(DefaultPageSize);
    }

    private void LoadController_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncLoadState();
    }

    private void Rows_LoadCompleted(object? sender, EventArgs e)
    {
        RefreshVisibleRows();
        QueueScope = BuildQueueScope(Container);
        SyncLoadState();
    }

    private void Rows_LoadFailed(object? sender, Exception exception)
    {
        _notification.ShowMessage(Rows.Count == 0 ? "加载列表失败" : "加载更多失败", exception.Message);
    }

    private void SyncLoadState()
    {
        IsLoading = _loadController.IsLoading;
        HasMore = _loadController.HasMore;
        State.CanRetry = _loadController.CanRetry;
    }

    private void RefreshVisibleRows()
    {
        var filterText = FilterBox?.Text ?? string.Empty;
        VisibleRows.Clear();
        VisibleRows.AddRange(Rows.Where(row => row.MatchesFilter(filterText)));

        RebuildGroups();
        UpdateCurrentItem(_state.NowPlayingProviderItem);
    }

    private void RebuildGroups()
    {
        GroupedItems.Clear();
        State.ActiveItemsSource = string.IsNullOrWhiteSpace(FilterBox?.Text)
            ? State.RowsView
            : State.VisibleRowsView;
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
            || !_loadController.CanAutoLoad
            || _loadController.IsLoading)
            return;

        LoadNextPageAsync().SafeFireAndForget();
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
        if (!GreedyLoad || !_loadController.HasMore || _loadController.CanRetry)
            return;

        _eagerLoadCts = new CancellationTokenSource();
        RunEagerLoadingAsync(_eagerLoadCts.Token).SafeFireAndForget();
    }

    private async Task RunEagerLoadingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (GreedyLoad && _loadController.CanAutoLoad)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await _rows.LoadInitialAsync(DefaultPageSize, cancellationToken);
                if (_loadController.CanRetry)
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
