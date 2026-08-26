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
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Comments;
using HyPlayer.Platform.Xaml;
using HyPlayer.Platform.Runtime.Background;
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
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();


#nullable enable
    private ScrollViewer? _mainScroll, _hotCommentsScroll;
#nullable restore
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Dictionary<string, Task> _commentLoadTasks = [];
    private readonly ObservableList<CommentBase> _hotComments = [];
    private readonly NotifyCollectionChangedSynchronizedViewList<CommentBase> _hotCommentsView;
    private readonly ObservableList<CommentBase> _normalComments = [];
    private readonly NotifyCollectionChangedSynchronizedViewList<CommentBase> _normalCommentsView;
    private int _delayedUiVersion;
    private int _hotCommentsLoadVersion;
    private bool _isUnloaded;
    private int _normalCommentsLoadVersion;
    private string _cursor;
    private bool _isShiftingPage;
    private int _page = 1;
    private string _resourceId;
    private string _resourceType;
    private int _sortType = 1;

    public Comments()
    {
        _hotCommentsView = _hotComments.ToNotifyCollectionChanged();
        _normalCommentsView = _normalComments.ToNotifyCollectionChanged();
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
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

        StartLoadComments(2);
        StartLoadComments(_sortType);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Bindings.StopTracking();
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
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

        var offset = type == 3 && _page != 1 && int.TryParse(_cursor, out var cursorOffset)
            ? cursorOffset
            : (_page - 1) * 20;
        var result = await LoadProviderCommentsAsync(offset, type);
        if (targetHotComments
                ? requestVersion != _hotCommentsLoadVersion
                : requestVersion != _normalCommentsLoadVersion)
            return;

        if (targetHotComments)
            _hotComments.Clear();
        else _normalComments.Clear();

        foreach (var comment in result.Items)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            comment.ProvidableItemId = CreateProvidableItemId(_resourceType, _resourceId);
        }

        if (targetHotComments)
            _hotComments.AddRange(result.Items);
        else
            _normalComments.AddRange(result.Items);

        if (type == 3)
            _cursor = result.NextOffset?.ToString();

        NextPage.IsEnabled = result.HasMore;
        PrevPage.IsEnabled = _page > 1;
    }

    private void StartLoadComments(int type)
    {
        if (string.IsNullOrEmpty(_resourceId))
            return;

        var offset = type == 3 && _page != 1 && int.TryParse(_cursor, out var cursorOffset)
            ? cursorOffset
            : (_page - 1) * 20;
        var key = $"{_resourceId}:{_resourceType}:{_page}:{offset}:{type}";
        if (_commentLoadTasks.TryGetValue(key, out var runningTask) && !runningTask.IsCompleted)
            return;

        var task = LoadComments(type);
        _commentLoadTasks[key] = task;
        _taskRunner.Forget(task, "load comments");
        _ = RemoveCommentLoadTaskWhenCompletedAsync(key, task);
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

    private async Task<ProviderPageResult<CommentBase>> LoadProviderCommentsAsync(int offset, int type)
    {
        try
        {
            return await _commentProvider.GetCommentsAsync(
                _resourceId,
                _resourceType,
                offset,
                20,
                type,
                _cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("加载评论时出错", ex.Message);
            return new ProviderPageResult<CommentBase>
            {
                Items = [],
                HasMore = false
            };
        }
    }

    private string CreateProvidableItemId(string typeId, string actualId)
    {
        return _knownTypeIds.Id + typeId + actualId;
    }


    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        StartLoadComments(_sortType);
        ScrollTop();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        _page--;
        StartLoadComments(_sortType);
        ScrollTop();
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
        _cursor = null;
        StartLoadComments(_sortType);
    }

    private void SkipPage_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PageSelect.Text, out _page))
        {
            StartLoadComments(_sortType);
            ScrollTop();
        }
    }

    private void ScrollTop()
    {
        var delayedUiVersion = _delayedUiVersion;
        var transform = AllCmtsTB.TransformToVisual(_mainScroll);
        var point = transform.TransformPoint(new Point(0, -1000000)); //一定要这么大
        var y = point.Y + _mainScroll.VerticalOffset;
        _mainScroll.ChangeView(null, y, null, false);
        var delay = TimeSpan.FromMilliseconds(320); //稍微等等再滚回去，免得回到热评区域
        var delayTimer = ThreadPoolTimer.CreateTimer(
            source =>
            {
                _ = this.RunOnUIThreadAsync(() =>
                {
                    if (!IsCurrentDelayedUi(delayedUiVersion)) return;
                    point = transform.TransformPoint(new Point(0, 25)); //要超过判定区域，还要预留一点
                    y = point.Y + _mainScroll.VerticalOffset;
                    _mainScroll.ChangeView(null, y, null, false);
                });
            }, delay);
    }

    private void BackToTop_Click(object sender, RoutedEventArgs e)
    {
        ScrollTop();
    }

    private void MainScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var transform = AllCmtsTB.TransformToVisual(_mainScroll);
        var point = transform.TransformPoint(new Point(0, 0));
        var y = point.Y + _mainScroll.VerticalOffset;
        if ((sender?.As<ScrollViewer>()).VerticalOffset > y + 25)
            BackToTop.Visibility = Visibility.Visible;
        else BackToTop.Visibility = Visibility.Collapsed;
        if ((sender?.As<ScrollViewer>()).VerticalOffset < 15)
        {
            var delayedUiVersion = _delayedUiVersion;
            var delay = TimeSpan.FromMilliseconds(90); //先别急，如果是回到顶部触发的会滚回去一点
            var delayTimer = ThreadPoolTimer.CreateTimer(
                source =>
                {
                    _ = this.RunOnUIThreadAsync(() =>
                    {
                        if (!IsCurrentDelayedUi(delayedUiVersion)) return;
                        if ((sender?.As<ScrollViewer>()).VerticalOffset < 15)
                            ShiftCommentList(false); //回到热评
                    });
                }, delay);
        }
    }

    private void PageSelect_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (int.TryParse(PageSelect.Text, out _page))
        {
            StartLoadComments(_sortType);
            ScrollTop();
        }
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
