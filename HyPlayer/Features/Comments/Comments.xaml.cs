using System;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Domain.Comments;
using HyPlayer.Platform.Xaml;

namespace HyPlayer.Features.Comments;

public sealed partial class Comments : Page
{
    private readonly INotificationService _notification =
        Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private ScrollViewer? _mainScroll;

    public CommentsViewModel ViewModel { get; } =
        Ioc.Default.GetRequiredService<CommentsViewModel>();

    public Comments()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        CommentTarget? target = e.Parameter switch
        {
            CommentTarget value => value,
            string value when CommentTarget.TryParseExternalResource(value, out var parsed) => parsed,
            _ => null
        };
        if (target is not null)
            _taskRunner.Forget(
                ViewModel.LoadAsync(target, _cancellationTokenSource.Token),
                "load comments page");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        Bindings.StopTracking();
    }

    private void MainComments_Loaded(object sender, RoutedEventArgs e)
    {
        AttachMainScroll(NormalComments.CommentPresentScrollViewer);
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(
            ViewModel.NextPageAsync(_cancellationTokenSource.Token),
            "load next comments page");
        ScrollTop();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(
            ViewModel.PreviousPageAsync(_cancellationTokenSource.Token),
            "load previous comments page");
        ScrollTop();
    }

    private void ComboBoxSortType_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxSortType is null)
            return;
        _taskRunner.Forget(
            ViewModel.ChangeSortTypeAsync(
                ComboBoxSortType.SelectedIndex + 1,
                _cancellationTokenSource.Token),
            "change comment sort mode");
    }

    private void PageSelect_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!int.TryParse(PageSelect.Text, out var page))
            return;
        _taskRunner.Forget(
            ViewModel.GoToPageAsync(page, _cancellationTokenSource.Token),
            "skip comments page");
        ScrollTop();
    }

    private void SendComment_Click(object sender, RoutedEventArgs e)
    {
        _notification.ShowMessage("评论功能暂时关闭", "由于网易云音乐风控策略，评论功能暂时关闭");
    }

    private void BackToTop_Click(object sender, RoutedEventArgs e)
    {
        ScrollTop();
    }

    private void MainScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            BackToTop.Visibility = scrollViewer.VerticalOffset > 25
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        AttachMainScroll(null);
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

    private void ScrollTop()
    {
        _mainScroll?.ChangeView(null, 0, null, false);
    }
}
