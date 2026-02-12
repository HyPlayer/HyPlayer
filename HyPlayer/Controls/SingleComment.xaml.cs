#region

using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Comment;
using HyPlayer.Pages;
using ObservableCollections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class SingleComment : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty AvatarSourceProperty =
        DependencyProperty.Register("AvatarSource", typeof(BitmapImage), typeof(SingleComment),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MainCommentProperty =
        DependencyProperty.Register("MainComment", typeof(Comment), typeof(SingleComment),
            new PropertyMetadata(null)); //主评论

    public event PropertyChangedEventHandler PropertyChanged;

    public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
    }


    private ObservableList<Comment> floorComments = new ObservableList<Comment>();
    public UserDisplay CommentUserDisplay;
    private string time = "0";

    public SingleComment()
    {
        InitializeComponent();
    }


    public BitmapImage AvatarSource
    {
        get => (BitmapImage)GetValue(AvatarSourceProperty);
        set => SetValue(AvatarSourceProperty, value);
    }

    public Comment MainComment
    {
        get => (Comment)GetValue(MainCommentProperty);
        set
        {
            SetValue(MainCommentProperty, value);
            ReplyCountIndicator.Text = value.ReplyCount.ToString();
            LikeCountTB.Text = value.LikedCount.ToString();
        }
    }

    private async Task LoadFloorComments(bool IsLoadMoreComments)
    {
        try
        {
            if (!IsLoadMoreComments) floorComments.Clear();
            var result = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Comments, $"{MainComment.ResourceType}_{MainComment.ResourceId}_{MainComment.CommentId}", async () =>
            {
                var rst = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.CommentFloorApi,
                    new CommentFloorRequest()
                    {
                        ParentCommentId = MainComment.CommentId,
                        ResourceId = MainComment.ResourceId,
                        ResourceType = MainComment.ResourceType,
                        Time = !IsLoadMoreComments ? 0 : long.Parse(time ?? "0")
                    }
                );
                if (rst.IsError)
                {
                    Common.AddToTeachingTipLists("加载楼层评论错误", rst.Error?.Message ?? "未知错误");
                    return null;
                }
                return rst.Value;
            }, TimeSpan.FromMinutes(5));
            if (result == null)
            {
                return;
            }
            var list = new List<Comment>(result.Data?.Comments.Length ?? 0);
            foreach (var floorcomment in result.Data?.Comments ?? [])
            {
                var floorComment = floorcomment.MapToComment();
                floorComment.ResourceId = MainComment.ResourceId;
                floorComment.ResourceType = MainComment.ResourceType;
                floorComment.IsMainComment = false;
                list.Add(floorComment);
            }
            floorComments.AddRange(list);
            time = result?.Data?.Time.ToString();
            LoadMore.Visibility = result?.Data?.HasMore is true ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.CommentLikeApi,
            new CommentLikeRequest()
            {
                CommentId = MainComment.CommentId,
                ResourceType = MainComment.ResourceType,
                IsLike = !MainComment.HasLiked
            });
        if (result.IsError)
        {
            Common.AddToTeachingTipLists("点赞失败", result.Error?.Message ?? "未知错误");
            return;
        }

        MainComment.LikedCount += MainComment.HasLiked ? -1 : 1;
        MainComment.HasLiked = !MainComment.HasLiked;
        LikeCountTB.Text = MainComment.LikedCount.ToString();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
        // TODO: 删除评论
    }

    private void NavToUser_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(Me), MainComment.CommentUser.Id);
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ReplyText.Text) && Common.Logined)
        {
            try
            {
                // TODO: 发送评论
                ReplyText.Text = string.Empty;
                await Task.Delay(1000);
                _ = LoadFloorComments(false);
            }
            catch (Exception ex)
            {
                var dlg = new MessageDialog(ex.Message, "出现问题，评论失败");
                await dlg.ShowAsync();
            }
        }
        else if (string.IsNullOrWhiteSpace(ReplyText.Text))
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

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadFloorComments(true);
    }


    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        CommentUserDisplay = new(MainComment.CommentUser);
        ReplyBtn.Visibility = Visibility.Visible;
        FloorCommentsExpander.Visibility = MainComment.IsMainComment ? Visibility.Visible : Visibility.Collapsed;
        Bindings.Update();
    }

    private void FloorCommentsExpander_Expanding(Microsoft.UI.Xaml.Controls.Expander sender,
        Microsoft.UI.Xaml.Controls.ExpanderExpandingEventArgs args)
    {
        _ = LoadFloorComments(false);
    }

    private void FloorCommentsExpander_Collapsed(Microsoft.UI.Xaml.Controls.Expander sender,
        Microsoft.UI.Xaml.Controls.ExpanderCollapsedEventArgs args)
    {
        floorComments.Clear();
    }
}