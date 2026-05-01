#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.ViewModels;
using System;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using WinRT;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了"空白页"项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Comments : Page
{
#nullable enable
    private ScrollViewer? MainScroll, HotCommentsScroll;
#nullable restore
    private bool IsShiftingPage = false;

    public Comments()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<CommentsViewModel>();
    }

    private CommentsViewModel ViewModel => (CommentsViewModel)DataContext;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string resstr)
        {
            ViewModel.Initialize(resstr);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.CleanupAsync();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommentsViewModel viewModel)
        {
            viewModel.NextPage();
            ScrollTop();
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommentsViewModel viewModel)
        {
            viewModel.PrevPage();
            ScrollTop();
        }
    }

    private void SendComment_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 评论功能风控
        Common.AddToTeachingTipLists("评论功能暂时关闭", "由于网易云音乐风控策略，评论功能暂时关闭");
    }

    private void ComboBoxSortType_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is CommentsViewModel viewModel)
            viewModel.ChangeSort(ComboBoxSortType.SelectedIndex);
    }

    private void SkipPage_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PageSelect.Text, out int pageNumber) && DataContext is CommentsViewModel viewModel)
        {
            viewModel.SkipPage(pageNumber);
            ScrollTop();
        }
    }

    private void ScrollTop()
    {
        var transform = AllCmtsTB.TransformToVisual(MainScroll);
        var point = transform.TransformPoint(new Point(0, -1000000));//一定要这么大
        var y = point.Y + MainScroll.VerticalOffset;
        MainScroll.ChangeView(null, y, null, false);
        TimeSpan delay = TimeSpan.FromMilliseconds(320);//稍微等等再滚回去，免得回到热评区域
        ThreadPoolTimer DelayTimer = ThreadPoolTimer.CreateTimer(
    (source) =>

    {
        _ = Common.Invoke(
        () =>
        {
            point = transform.TransformPoint(new Point(0, 25));//要超过判定区域，还要预留一点
            y = point.Y + MainScroll.VerticalOffset;
            MainScroll.ChangeView(null, y, null, false);
        });

    }, delay);

    }

    private void BackToTop_Click(object sender, RoutedEventArgs e)
    {
        ScrollTop();
    }

    private void MainScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var transform = AllCmtsTB.TransformToVisual(MainScroll);
        var point = transform.TransformPoint(new Point(0, 0));
        var y = point.Y + MainScroll.VerticalOffset;
        if ((sender?.As<ScrollViewer>()).VerticalOffset > y + 25)
            BackToTop.Visibility = Visibility.Visible;
        else BackToTop.Visibility = Visibility.Collapsed;
        if ((sender?.As<ScrollViewer>()).VerticalOffset < 15)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(90);//先别急，如果是回到顶部触发的会滚回去一点
            ThreadPoolTimer DelayTimer = ThreadPoolTimer.CreateTimer(
        (source) =>

        {
            _ = Common.Invoke(
            () =>
            {
                if ((sender?.As<ScrollViewer>()).VerticalOffset < 15)
                    ShiftCommentList(false);//回到热评
            });

        }, delay);
        }
    }

    private void PageSelect_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (int.TryParse(PageSelect.Text, out int pageNumber) && DataContext is CommentsViewModel viewModel)
        {
            viewModel.SkipPage(pageNumber);
            ScrollTop();
        }
    }

    private void HotComments_Loaded(object sender, RoutedEventArgs e)
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(500);
        ThreadPoolTimer DelayTimer = ThreadPoolTimer.CreateTimer(
    (source) =>

        {
            _ = Common.Invoke(
            () =>
           {
               HotCommentsScroll = HotComments.CommentPresentScrollViewer;
               HotCommentsScroll.ViewChanged += HotCommentsScroll_ViewChanged;
           });

        }, delay);//缓一会再加载，要不然获取不到

    }

    private void HotCommentsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (HotCommentsScroll.ScrollableHeight - HotCommentsScroll.VerticalOffset <= 14)
            ShiftCommentList(true);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        HotCommentsScroll?.ViewChanged -= HotCommentsScroll_ViewChanged;
        MainScroll?.ViewChanged -= MainScroll_ViewChanged;
    }

    private void ShiftCommentList(bool direction)
    {
        IsShiftingPage = true;
        if (direction)
        {
            AllCommentsContainer.Visibility = Visibility.Visible;
            var animation = (Storyboard)Resources["CommentFlyUp"];
            animation.Begin();
            HotCommentsContainer.Visibility = Visibility.Collapsed;
            TimeSpan delay = TimeSpan.FromMilliseconds(500);
            ThreadPoolTimer DelayTimer = ThreadPoolTimer.CreateTimer(
            (source) =>

            {
                _ = Common.Invoke(
                () =>
                {
                    MainScroll = NormalComments.CommentPresentScrollViewer;
                    var transform = AllCmtsTB.TransformToVisual(MainScroll);
                    var point = transform.TransformPoint(new Point(0, 25));//要超过判定区域，还要预留一点
                    var y = point.Y + MainScroll.VerticalOffset;
                    MainScroll.ChangeView(null, y, null, false);
                    MainScroll.ViewChanged += MainScroll_ViewChanged;
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
        IsShiftingPage = false;
    }
}
