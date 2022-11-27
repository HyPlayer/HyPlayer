﻿#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using System.Collections.ObjectModel;
using Windows.UI.Xaml.Media;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml.Media.Animation;
using System.Drawing;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Comments : Page, IDisposable
{
    private string cursor;
    private int page = 1;
    private string resourceid;
    private int resourcetype;
    private int sortType = 1;
    private ScrollViewer MainScroll, HotCommentsScroll;
    private ObservableCollection<Comment> hotComments = new ObservableCollection<Comment>();
    private ObservableCollection<Comment> normalComments = new ObservableCollection<Comment>();

    public Comments()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        hotComments.Clear();
        normalComments.Clear();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string resstr)
        {
            resourceid = resstr.Substring(2);
            switch (resstr.Substring(0, 2))
            {
                case "sg":
                    resourcetype = 0;
                    break;
                case "mv":
                    resourcetype = 1;
                    break;
                case "fm":
                    resourcetype = 4;
                    break;
                case "mb":
                    resourcetype = 7;
                    break;
                case "al":
                    resourcetype = 3;
                    break;
                case "pl":
                    resourcetype = 2;
                    break;
            }
        }

        LoadHotComments();
        _ = LoadComments(sortType);
    }


    private void LoadHotComments()
    {
        _ = LoadComments(2);
    }

    private async Task LoadComments(int type)
    {
        // type 1:按推荐排序,2:按热度排序,3:按时间排序
        if (string.IsNullOrEmpty(resourceid)) return;
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentNew,
                new Dictionary<string, object>
                {
                    { "cursor", page != 1 && type == 3 ? cursor : null },
                    { "id", resourceid },
                    { "type", resourcetype },
                    { "pageNo", page },
                    { "pageSize", 20 },
                    { "sortType", type }
                });
            if (type == 2)
                hotComments.Clear();
            else normalComments.Clear();
            foreach (var comment in json["data"]["comments"].ToArray())
            {
                Comment LoadedComment = Comment.CreateFromJson(comment, resourceid, resourcetype);
                if (type == 2)
                    hotComments.Add(LoadedComment);
                else normalComments.Add(LoadedComment);
            }
            if (type == 3)
                cursor = json["data"]["cursor"].ToString();
            if (json["data"]["hasMore"].ToString() == "True")
                NextPage.IsEnabled = true;
            else
                NextPage.IsEnabled = false;

            if (page > 1)
                PrevPage.IsEnabled = true;
            else
                PrevPage.IsEnabled = false;

            PageIndicator.Text =
                $"第 {page} 页 / 共 {Math.Ceiling((decimal)json["data"]["totalCount"].ToObject<long>() / 20).ToString()} 页";
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        page++;
        _ = LoadComments(sortType);
        ScrollTop();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        page--;
        _ = LoadComments(sortType);
        ScrollTop();
    }

    private async void SendComment_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CommentEdit.Text) && Common.Logined)
        {
            try
            {
                await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
                    new Dictionary<string, object>
                    {
                        {
                            "id", resourceid
                        },
                        {
                            "type", resourcetype
                        },
                        {
                            "t", "1"
                        },
                        {
                            "content", CommentEdit.Text
                        }
                    });
                CommentEdit.Text = string.Empty;
                await Task.Delay(1000);
                _ = LoadComments(3);
            }
            catch (Exception ex)
            {
                var dlg = new MessageDialog(ex.Message, "出现问题，评论失败");
                await dlg.ShowAsync();
            }
        }

        else if (string.IsNullOrWhiteSpace(CommentEdit.Text))
        {
            var dlg = new MessageDialog("评论不能为空");
            await dlg.ShowAsync();
        }
        else
        {
            var dlg = new MessageDialog("请先登录");
            await dlg.ShowAsync();
        }
    }

    private void ComboBoxSortType_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        sortType = ComboBoxSortType.SelectedIndex + 1;
        _ = LoadComments(sortType);
    }

    private void SkipPage_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PageSelect.Text, out page))
        {
            _ = LoadComments(sortType);
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
        Dispatcher.RunAsync(
        CoreDispatcherPriority.Low,
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
        if ((sender as ScrollViewer).VerticalOffset > y + 25)
            BackToTop.Visibility = Visibility.Visible;
        else BackToTop.Visibility = Visibility.Collapsed;
        if ((sender as ScrollViewer).VerticalOffset < 15)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(90);//先别急，如果是回到顶部触发的会滚回去一点
            ThreadPoolTimer DelayTimer = ThreadPoolTimer.CreateTimer(
        (source) =>

        {
            Dispatcher.RunAsync(
            CoreDispatcherPriority.Low,
            () =>
            {
                if ((sender as ScrollViewer).VerticalOffset < 15)
                    ShiftCommentList(false);//回到热评
            });

        }, delay);
        }
    }

    private void PageSelect_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (int.TryParse(PageSelect.Text, out page))
        {
            _ = LoadComments(sortType);
            ScrollTop();
        }
    }

    private void HotComments_Loaded(object sender, RoutedEventArgs e)
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(500);
        ThreadPoolTimer DelayTimer = ThreadPoolTimer.CreateTimer(
    (source) =>

        {
            Dispatcher.RunAsync(
            CoreDispatcherPriority.Low,
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

    private void ShiftCommentList(bool direction)
    {
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
                Dispatcher.RunAsync(
                CoreDispatcherPriority.Low,
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
    }

}