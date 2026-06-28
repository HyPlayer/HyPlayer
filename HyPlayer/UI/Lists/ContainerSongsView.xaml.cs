using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.UI.Lists;

public sealed partial class ContainerSongsView : UserControl
{
    private const int DefaultPageSize = 500;
    private const string DailyRecommendContainerTypeId = "daily";

    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly WeakEventListener<ContainerSongsView, object?, EventArgs> _secondTickListener;
    private Button _flatLoadMoreButton;
    private ProgressRing _flatFooterProgressRing;
    private StackPanel _flatFooterPanel;
    private Button _groupedLoadMoreButton;
    private ProgressRing _groupedFooterProgressRing;
    private StackPanel _groupedFooterPanel;
    private CancellationTokenSource? _loadCts;
    private IProgressiveLoadingContainer? _progressiveContainer;
    private UndeterminedContainerBase? _undeterminedContainer;
    private bool _isSecondTickSubscribed;
    private int _greedyLoadThreshold = 3;
    private int _nextOffset;

    public static readonly DependencyProperty ContainerProperty = DependencyProperty.Register(
        nameof(Container), typeof(ContainerBase), typeof(ContainerSongsView),
        new PropertyMetadata(default(ContainerBase), OnContainerChanged));

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        nameof(ListHeader), typeof(UIElement), typeof(ContainerSongsView),
        new PropertyMetadata(default(UIElement), OnListHeaderChanged));

    public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
        nameof(IsSearchEnabled), typeof(bool), typeof(ContainerSongsView), new PropertyMetadata(false));

    public static readonly DependencyProperty CanViewCommentsProperty = DependencyProperty.Register(
        nameof(CanViewComments), typeof(bool), typeof(ContainerSongsView), new PropertyMetadata(false));

    public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
        nameof(IsMySongList), typeof(bool), typeof(ContainerSongsView), new PropertyMetadata(false));

    public static readonly DependencyProperty GreedyLoadProperty = DependencyProperty.Register(
        nameof(GreedyLoad), typeof(bool), typeof(ContainerSongsView), new PropertyMetadata(false, OnGreedyLoadChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(ContainerSongsView), new PropertyMetadata(false));

    public static readonly DependencyProperty HasMoreProperty = DependencyProperty.Register(
        nameof(HasMore), typeof(bool), typeof(ContainerSongsView), new PropertyMetadata(false, OnFooterStateChanged));

    public static readonly DependencyProperty IsGroupedProperty = DependencyProperty.Register(
        nameof(IsGrouped), typeof(bool), typeof(ContainerSongsView),
        new PropertyMetadata(false, OnDisplayModeChanged));

    public static readonly DependencyProperty QueueScopeProperty = DependencyProperty.Register(
        nameof(QueueScope), typeof(SongListQueueScope), typeof(ContainerSongsView),
        new PropertyMetadata(SongListQueueScope.Visible));

    public ObservableCollection<SongListItemViewModel> Songs { get; } = [];
    public ObservableCollection<SongListItemGroup> GroupedSongs { get; } = [];

    public ContainerSongsView()
    {
        (_flatFooterPanel, _flatFooterProgressRing, _flatLoadMoreButton) = CreateFooter();
        (_groupedFooterPanel, _groupedFooterProgressRing, _groupedLoadMoreButton) = CreateFooter();

        InitializeComponent();

        _secondTickListener = new WeakEventListener<ContainerSongsView, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.GreedyLoadNextPage(),
            OnDetachAction = weakEventListener => { _globalTimer.SecondTick -= weakEventListener.OnEvent; }
        };

        UpdateFooter();
        UpdateActiveListChrome();
        Bindings.Update();
    }

    private void ContainerSongsView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachSecondTick();
        _loadCts?.Cancel();
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

    public bool IsSearchEnabled
    {
        get => (bool)GetValue(IsSearchEnabledProperty);
        set => SetValue(IsSearchEnabledProperty, value);
    }

    public bool CanViewComments
    {
        get => (bool)GetValue(CanViewCommentsProperty);
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

    public bool IsGrouped
    {
        get => (bool)GetValue(IsGroupedProperty);
        private set => SetValue(IsGroupedProperty, value);
    }

    public Visibility FlatListVisibility => IsGrouped ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GroupedListVisibility => IsGrouped ? Visibility.Visible : Visibility.Collapsed;
    public bool IsInitialLoading => IsLoading && Songs.Count == 0;
    public SongListQueueScope QueueScope
    {
        get => (SongListQueueScope)GetValue(QueueScopeProperty);
        private set => SetValue(QueueScopeProperty, value);
    }

    public IReadOnlyList<SingleSongBase> LoadedProviderSongs => Songs.Select(song => song.ToProviderSong()).ToList();

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
        await _playCore.StopAsync();
        await _playCore.RemoveAllSongAsync();

        if (BuildMusicResource(Container) is { } resource)
        {
            await _navigator.AppendAsync(resource);
        }
        else
        {
            await _playCore.InsertSongRangeAsync(LoadedProviderSongs.ToList());
        }

        await _control.MoveNextAndPlayAsync(userInitiated: true);
    }

    public async Task AddAllToPlaylistAsync()
    {
        if (BuildMusicResource(Container) is { } resource)
            await _navigator.AppendAsync(resource);
        else
            await _playCore.InsertSongRangeAsync(LoadedProviderSongs.ToList());
    }

    public void DownloadAllLoaded()
    {
        DownloadManager.AddDownload(LoadedProviderSongs.ToList());
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateFooter();
    }

    private static void OnContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ContainerSongsView)d).StartLoadForContainer();
    }

    private static void OnGreedyLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ContainerSongsView)d;
        if ((bool)e.NewValue && view.HasMore)
            view.AttachSecondTick();
        else if (!(bool)e.NewValue)
            view.DetachSecondTick();
    }

    private static void OnFooterStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ContainerSongsView)d;
        view.UpdateFooter();
        view.Bindings.Update();
        if (view.GreedyLoad && view.HasMore)
            view.AttachSecondTick();
        else if (!view.HasMore)
            view.DetachSecondTick();
    }

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ContainerSongsView)d;
        view.UpdateActiveListChrome();
        view.Bindings.Update();
    }

    private static void OnListHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ContainerSongsView)d).UpdateActiveListChrome();
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
        Songs.Clear();
        GroupedSongs.Clear();
        IsGrouped = Container is AlbumBase;
        QueueScope = BuildQueueScope(Container);
        LoadFirstPageAsync(_loadCts.Token).SafeFireAndForget();
    }

    private async Task LoadFirstPageAsync(CancellationToken cancellationToken)
    {
        if (Container is null)
            return;

            IsLoading = true;
            Bindings.Update();
            UpdateFooter();
            try
        {
            if (_progressiveContainer is not null)
            {
                await LoadProgressivePageAsync(cancellationToken);
            }
            else if (_undeterminedContainer is not null)
            {
                await LoadUndeterminedPageAsync(cancellationToken);
            }
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
            _notification.ShowMessage("加载歌曲失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
            Bindings.Update();
            UpdateFooter();
        }
    }

    private async Task LoadNextPageAsync()
    {
        if (IsLoading || !HasMore)
            return;

        var cancellationToken = _loadCts?.Token ?? CancellationToken.None;
        IsLoading = true;
        Bindings.Update();
        UpdateFooter();
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
            Bindings.Update();
            UpdateFooter();
        }
    }

    private async Task LoadProgressivePageAsync(CancellationToken cancellationToken)
    {
        if (_progressiveContainer is null)
            return;

        var pageSize = GetPageSize(_progressiveContainer);
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
        foreach (var song in items.OfType<SingleSongBase>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await SongListItemViewModel.FromProviderSongAsync(song, Songs.Count + 1);
            Songs.Add(row);
            Bindings.Update();
        }

        if (IsGrouped)
            RebuildGroups();
    }

    private void RebuildGroups()
    {
        GroupedSongs.Clear();
        foreach (var group in Songs.GroupBy(song => song.CDName).OrderBy(group => group.Key))
        {
            GroupedSongs.Add(new SongListItemGroup(group) { Key = group.Key });
        }

        Bindings.Update();
    }

    private static int GetPageSize(IProgressiveLoadingContainer container)
    {
        return Math.Clamp(container.MaxProgressiveCount, 1, DefaultPageSize);
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

    private void UpdateFooter()
    {
        if (_flatLoadMoreButton is null || _flatFooterProgressRing is null)
            return;

        UpdateFooter(_flatFooterProgressRing, _flatLoadMoreButton);
        UpdateFooter(_groupedFooterProgressRing, _groupedLoadMoreButton);
    }

    private void UpdateActiveListChrome()
    {
        if (FlatList is null || GroupedList is null)
            return;

        if (IsGrouped)
        {
            FlatList.ListHeader = null;
            FlatList.Footer = null;
            GroupedList.ListHeader = ListHeader;
            GroupedList.Footer = _groupedFooterPanel;
        }
        else
        {
            GroupedList.ListHeader = null;
            GroupedList.Footer = null;
            FlatList.ListHeader = ListHeader;
            FlatList.Footer = _flatFooterPanel;
        }
    }

    private (StackPanel Panel, ProgressRing ProgressRing, Button Button) CreateFooter()
    {
        var loadMoreButton = new Button
        {
            Content = "加载更多",
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = (Style)Application.Current.Resources["ButtonRevealStyle"]
        };
        loadMoreButton.Click += (_, _) => LoadMoreAsync().SafeFireAndForget();

        var progressRing = new ProgressRing
        {
            Width = 32,
            Height = 32,
            IsActive = false,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        panel.Children.Add(progressRing);
        panel.Children.Add(loadMoreButton);
        return (panel, progressRing, loadMoreButton);
    }

    private void UpdateFooter(ProgressRing progressRing, Button loadMoreButton)
    {
        progressRing.IsActive = IsLoading && HasMore;
        progressRing.Visibility = IsLoading && HasMore ? Visibility.Visible : Visibility.Collapsed;
        loadMoreButton.Visibility = HasMore && !IsLoading ? Visibility.Visible : Visibility.Collapsed;
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
}
