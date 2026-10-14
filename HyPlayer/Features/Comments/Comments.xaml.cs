#region

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Comments;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using ObservableCollections;
using WinRT;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Comments;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Comments : Page
{
    private readonly IProvidableItemCommentProvidable _commentProvider =
        Ioc.Default.GetRequiredService<IProvidableItemCommentProvidable>();

    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

#nullable enable
    private ScrollViewer? _mainScroll, _hotCommentsScroll;
#nullable restore
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Dictionary<string, Task> _commentLoadTasks = [];
    private readonly ObservableList<CommentBase> _hotComments = [];
    private readonly LoadState _hotLoad;
    private readonly LoadState _normalLoad;
    private readonly NotifyCollectionChangedSynchronizedViewList<CommentBase> _hotCommentsView;
    private readonly ObservableList<CommentBase> _normalComments = [];
    private readonly NotifyCollectionChangedSynchronizedViewList<CommentBase> _normalCommentsView;
    private Task _commentLoaderTask;
    private int _delayedUiVersion;
    private DateTime _suppressHotSwitchUntil;
    private Task _hotCommentLoaderTask;
    private int _hotCommentsLoadVersion;
    private bool _isUnloaded;
    private int _normalCommentsLoadVersion;
    private CommentCursorTrail _pageTrail = new();
    private bool _isShiftingPage;
    private int _page = 1;
    private string _resourceId;
    private string _resourceType;
    private int _sortType = 1;
    private int? _totalCount;

    public Comments()
    {
        _hotCommentsView = _hotComments.ToNotifyCollectionChanged();
        _normalCommentsView = _normalComments.ToNotifyCollectionChanged();
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
        var loadEnvironment = Ioc.Default.GetRequiredService<ILoadEnvironment>();
        _hotLoad = new LoadState("热评", loadEnvironment) { EmptyText = "还没有热评" };
        _normalLoad = new LoadState("评论", loadEnvironment) { EmptyText = "还没有评论" };
        _normalLoad.PropertyChanged += (_, _) => ShowNormalLoad();
        HotLoadView.State = _hotLoad;
        ShowNormalLoad();
    }

    private void ShowNormalLoad()
    {
        var isListEmpty = _normalComments.Count == 0;
        NormalLoadView.State = isListEmpty ? _normalLoad : null;
        PageIndicator.Text = isListEmpty
            ? string.Empty
            : !_normalLoad.IsLoading
                ? CommentPageText.Format(_page, _totalCount)
                : _normalLoad.IsSlow
                    ? LoadState.SlowText
                    : _normalLoad.Title;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isUnloaded = false;
        _delayedUiVersion++;
        if (e.Parameter is CommentTarget target)
        {
            _resourceId = target.ResourceId;
            _resourceType = target.TypeId;
        }
        else if (e.Parameter is string resstr && CommentTarget.TryParseExternalResource(resstr, out target))
        {
            _resourceId = target.ResourceId;
            _resourceType = target.TypeId;
        }

        LoadHotComments();
        _commentLoaderTask = StartLoadComments(_sortType);
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Bindings.StopTracking();
        _hotLoad.Cancel();
        _normalLoad.Cancel();
        if (_commentLoaderTask != null && !_commentLoaderTask.IsCompleted)
            try
            {
                _cancellationTokenSource.Cancel();
                await _commentLoaderTask;
            }
            catch
            {
            }

        if (_hotCommentLoaderTask != null && !_hotCommentLoaderTask.IsCompleted)
            try
            {
                _cancellationTokenSource.Cancel();
                await _hotCommentLoaderTask;
            }
            catch
            {
            }

        _cancellationTokenSource?.Dispose();
    }


    private void LoadHotComments()
    {
        _hotCommentLoaderTask = StartLoadComments(2);
    }

    private async Task LoadComments(int type)
    {
        if (string.IsNullOrEmpty(_resourceId)) return;
        if (_isShiftingPage) return;
        _cancellationToken.ThrowIfCancellationRequested();
        var isHotCommentsPage = HotCommentsContainer.Visibility == Visibility.Visible;
        var targetHotComments = type == 2 && isHotCommentsPage;
        var requestVersion = targetHotComments
            ? ++_hotCommentsLoadVersion
            : ++_normalCommentsLoadVersion;

        var load = targetHotComments ? _hotLoad : _normalLoad;
        var attempt = targetHotComments
            ? load.Begin(() =>
            {
                LoadHotComments();
                return _hotCommentLoaderTask;
            }, out _)
            : load.Begin(() => _commentLoaderTask = StartLoadComments(_sortType), out _);
        var page = _page;
        var openedPage = page;
        ProviderPageResult<CommentBase> result;
        try
        {
            if (targetHotComments)
                result = await LoadProviderCommentsAsync(page, null, type);
            else
                (result, openedPage) = await _pageTrail.OpenAsync(page,
                    (number, token) => LoadProviderCommentsAsync(number, token, type));
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!load.IsCurrent(attempt))
                return;

            if (!targetHotComments)
            {
                _normalComments.Clear();
                NextPage.IsEnabled = false;
                PrevPage.IsEnabled = _page > 1;
            }

            load.Fail(attempt, exception);
            return;
        }

        if (targetHotComments
                ? requestVersion != _hotCommentsLoadVersion
                : requestVersion != _normalCommentsLoadVersion)
            return;

        if (openedPage != page)
        {
            _page = openedPage;
            PageSelect.Text = openedPage.ToString();
            _notification.ShowMessage($"无法直接跳到第 {page} 页", $"此排序只能逐页加载，已停在第 {openedPage} 页");
        }

        if (targetHotComments)
            _hotComments.Clear();
        else _normalComments.Clear();

        foreach (var comment in result.Items)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            comment.ProvidableItemId = CreateProvidableItemId(_resourceType, _resourceId);
        }

        if (targetHotComments)
        {
            _hotComments.AddRange(result.Items);
        }
        else
        {
            _normalComments.AddRange(result.Items);
            _totalCount = result.TotalCount;
        }

        NextPage.IsEnabled = result.HasMore;
        PrevPage.IsEnabled = _page > 1;
        load.Complete(attempt, result.Items.Count > 0);
        if (!targetHotComments)
            ShowNormalLoad();
    }

    private Task StartLoadComments(int type)
    {
        if (string.IsNullOrEmpty(_resourceId))
            return Task.CompletedTask;

        var key = $"{_resourceId}:{_resourceType}:{_page}:{type}";
        if (_commentLoadTasks.TryGetValue(key, out var runningTask) && !runningTask.IsCompleted)
            return runningTask;

        var task = LoadComments(type);
        _commentLoadTasks[key] = task;
        _ = RemoveCommentLoadTaskWhenCompletedAsync(key, task);
        return task;
    }

    private async Task RemoveCommentLoadTaskWhenCompletedAsync(string key, Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The caller observes cancellation/failure; this cache only owns task lifetime.
        }

        if (_commentLoadTasks.TryGetValue(key, out var cachedTask) && ReferenceEquals(cachedTask, task))
            _commentLoadTasks.Remove(key);
    }

    private Task<ProviderPageResult<CommentBase>> LoadProviderCommentsAsync(int page, string pageToken, int type)
    {
        return LoadRequestProbe.GuardAsync(
            () => pageToken is not null && _commentProvider is IProvidableItemCommentPageTokenProvidable tokenProvider
                ? tokenProvider.GetCommentsPageAsync(
                    _resourceId,
                    _resourceType,
                    pageToken,
                    CommentPageText.PageSize,
                    type,
                    _cancellationToken)
                : _commentProvider.GetCommentsAsync(
                    _resourceId,
                    _resourceType,
                    (page - 1) * CommentPageText.PageSize,
                    CommentPageText.PageSize,
                    type,
                    _cancellationToken),
            static result => result?.Items is not { Count: > 0 });
    }

    private string CreateProvidableItemId(string typeId, string actualId)
    {
        return _knownTypeIds.Id + typeId + actualId;
    }


    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        PageSelect.Text = string.Empty;
        _commentLoaderTask = StartLoadComments(_sortType);
        Ioc.Default.GetRequiredService<HyPlayer.Platform.Runtime.Background.IBackgroundTaskRunner>()
            .Forget(ScrollTopAsync(), "scroll comments to top");
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        _page--;
        PageSelect.Text = string.Empty;
        _commentLoaderTask = StartLoadComments(_sortType);
        Ioc.Default.GetRequiredService<HyPlayer.Platform.Runtime.Background.IBackgroundTaskRunner>()
            .Forget(ScrollTopAsync(), "scroll comments to top");
    }

    private void SendComment_Click(object sender, RoutedEventArgs e)
    {
        // NOTE: Comment risk control not yet implemented
        _notification.ShowMessage("评论功能暂时关闭", "由于网易云音乐风控策略，评论功能暂时关闭");
    }

    private void ComboBoxSortType_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _sortType = ComboBoxSortType.SelectedIndex + 1;
        _page = 1;
        _pageTrail = new CommentCursorTrail();
        PageSelect?.Text = string.Empty;
        _commentLoaderTask = StartLoadComments(_sortType);
    }

    private void SkipPage_Click(object sender, RoutedEventArgs e)
    {
        JumpToSelectedPage();
    }

    private void JumpToSelectedPage()
    {
        if (!int.TryParse(PageSelect.Text, out var page) || page < 1)
            return;

        _page = page;
        _commentLoaderTask = StartLoadComments(_sortType);
        Ioc.Default.GetRequiredService<HyPlayer.Platform.Runtime.Background.IBackgroundTaskRunner>()
            .Forget(ScrollTopAsync(), "scroll comments to top");
    }

    private async Task ScrollTopAsync()
    {
        var delayedUiVersion = _delayedUiVersion;
        _suppressHotSwitchUntil = DateTime.UtcNow.AddSeconds(1.5);
        _mainScroll?.ChangeView(null, 0, null, true);
        if (_commentLoaderTask is { } loader)
            try
            {
                await loader;
            }
            catch
            {
            }

        if (!IsCurrentDelayedUi(delayedUiVersion)) return;
        _suppressHotSwitchUntil = DateTime.UtcNow.AddSeconds(1.5);
        _mainScroll?.ChangeView(null, 0, null, true);
    }

    private void BackToTop_Click(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<HyPlayer.Platform.Runtime.Background.IBackgroundTaskRunner>()
            .Forget(ScrollTopAsync(), "scroll comments to top");
    }

    private void MainScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var transform = AllCmtsTB.TransformToVisual(_mainScroll);
        var point = transform.TransformPoint(new Point(0, 0));
        var y = point.Y + _mainScroll.VerticalOffset;
        if ((sender?.As<ScrollViewer>()).VerticalOffset > y + 25)
            BackToTop.Visibility = Visibility.Visible;
        else BackToTop.Visibility = Visibility.Collapsed;
        if (DateTime.UtcNow < _suppressHotSwitchUntil) return;
        if ((sender?.As<ScrollViewer>()).VerticalOffset < 15 &&
            AllCommentsContainer.Visibility == Visibility.Visible)
        {
            var delayedUiVersion = _delayedUiVersion;
            var delay = TimeSpan.FromMilliseconds(90); //先别急，如果是回到顶部触发的会滚回去一点
            var delayTimer = ThreadPoolTimer.CreateTimer(
                source =>
                {
                    _ = this.RunOnUIThreadAsync(() =>
                    {
                        if (!IsCurrentDelayedUi(delayedUiVersion)) return;
                        if (DateTime.UtcNow >= _suppressHotSwitchUntil &&
                            (sender?.As<ScrollViewer>()).VerticalOffset < 15)
                            ShiftCommentList(false); //回到热评
                    });
                }, delay);
        }
    }

    private void PageSelect_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        JumpToSelectedPage();
    }

    private void HotComments_Loaded(object sender, RoutedEventArgs e)
    {
        var delayedUiVersion = _delayedUiVersion;
        var delay = TimeSpan.FromMilliseconds(500);
        var delayTimer = ThreadPoolTimer.CreateTimer(
            source =>
            {
                _ = this.RunOnUIThreadAsync(() =>
                {
                    if (!IsCurrentDelayedUi(delayedUiVersion)) return;
                    AttachHotCommentsScroll(HotComments.CommentPresentScrollViewer);
                });
            }, delay); //缓一会再加载，要不然获取不到
    }

    private void HotCommentsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_hotCommentsScroll.ScrollableHeight - _hotCommentsScroll.VerticalOffset <= 14)
            ShiftCommentList(true);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _delayedUiVersion++;
        AttachHotCommentsScroll(null);
        AttachMainScroll(null);
    }

    private void AttachHotCommentsScroll(ScrollViewer? scrollViewer)
    {
        if (ReferenceEquals(_hotCommentsScroll, scrollViewer))
            return;

        if (_hotCommentsScroll is not null)
            _hotCommentsScroll.ViewChanged -= HotCommentsScroll_ViewChanged;

        _hotCommentsScroll = scrollViewer;

        if (_hotCommentsScroll is not null)
            _hotCommentsScroll.ViewChanged += HotCommentsScroll_ViewChanged;
    }

    private void AttachMainScroll(ScrollViewer? scrollViewer)
    {
        if (ReferenceEquals(_mainScroll, scrollViewer))
            return;

        if (_mainScroll is not null)
            _mainScroll.ViewChanged -= MainScroll_ViewChanged;

        _mainScroll = scrollViewer;

        if (_mainScroll is not null)
            _mainScroll.ViewChanged += MainScroll_ViewChanged;
    }

    private void ShiftCommentList(bool direction)
    {
        if (direction
                ? AllCommentsContainer.Visibility == Visibility.Visible
                : HotCommentsContainer.Visibility == Visibility.Visible)
            return;
        _isShiftingPage = true;
        if (direction)
        {
            AllCommentsContainer.Visibility = Visibility.Visible;
            var animation = (Storyboard)Resources["CommentFlyUp"];
            animation.Begin();
            HotCommentsContainer.Visibility = Visibility.Collapsed;
            var delayedUiVersion = _delayedUiVersion;
            var delay = TimeSpan.FromMilliseconds(500);
            var delayTimer = ThreadPoolTimer.CreateTimer(
                source =>
                {
                    _ = this.RunOnUIThreadAsync(() =>
                    {
                        if (!IsCurrentDelayedUi(delayedUiVersion)) return;
                        AttachMainScroll(NormalComments.CommentPresentScrollViewer);
                        var transform = AllCmtsTB.TransformToVisual(_mainScroll);
                        var point = transform.TransformPoint(new Point(0, 25)); //要超过判定区域，还要预留一点
                        var y = point.Y + _mainScroll.VerticalOffset;
                        _mainScroll.ChangeView(null, y, null, false);
                    });
                }, delay);
        }
        else
        {
            HotCommentsContainer.Visibility = Visibility.Visible;
            var animation = (Storyboard)Resources["CommentFlyDown"];
            animation.Begin();
            AllCommentsContainer.Visibility = Visibility.Collapsed;
            BackToTop.Visibility = Visibility.Collapsed;
        }

        _isShiftingPage = false;
    }

    private bool IsCurrentDelayedUi(int version)
    {
        return !_isUnloaded && version == _delayedUiVersion;
    }
}
