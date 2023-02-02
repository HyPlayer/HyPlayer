#region

using HyPlayer.Classes;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        new PropertyMetadata(null));//主评论

    public event PropertyChangedEventHandler PropertyChanged;

    public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
    }


    private ObservableCollection<Comment> floorComments = new ObservableCollection<Comment>();
    private Uri AvatarUri;
    private string time;

    public SingleComment()
    {
        InitializeComponent();
        floorComments.CollectionChanged += FloorComments_CollectionChanged;
    }

    private void FloorComments_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(floorComments));
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
            LikeCountTB.Text = value.likedCount.ToString();
        }
    }

    private async Task LoadFloorComments(bool IsLoadMoreComments)
    {
        try
        {
            if (!IsLoadMoreComments) floorComments.Clear();
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentFloor,
                new Dictionary<string, object>
                {
                    { "parentCommentId", MainComment.cid }, { "id", MainComment.resourceId },
                    { "type", MainComment.resourceType },
                    { "time", IsLoadMoreComments? 0 : time }
                });
            foreach (var floorcomment in json["data"]["comments"].ToArray())
            {
                var floorComment = Comment.CreateFromJson(floorcomment, MainComment.resourceId, MainComment.resourceType);
                floorComment.IsMainComment = false;
                floorComments.Add(floorComment);
            }
            time = json["data"]["time"].ToString();
            if (json["data"]["hasMore"].ToString() == "True")
                LoadMore.Visibility = Visibility.Visible;
            else LoadMore.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentLike,
            new Dictionary<string, object>
            {
                { "id", MainComment.resourceId }, { "cid", MainComment.cid }, { "type", MainComment.resourceType },
                { "t", MainComment.HasLiked ? "0" : "1" }
            });
        MainComment.likedCount += MainComment.HasLiked ? -1 : 1;
        MainComment.HasLiked = !MainComment.HasLiked;
        LikeCountTB.Text = MainComment.likedCount.ToString();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
            new Dictionary<string, object>
            {
                { "id", MainComment.resourceId }, { "t", "0" }, { "type", MainComment.resourceType },
                { "commentId", MainComment.cid }
            });
        (Parent as StackPanel).Children.Remove(this);
    }

    private void NavToUser_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(Me), MainComment.uid);
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ReplyText.Text) && Common.Logined)
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
                    new Dictionary<string, object>
                    {
                        { "id", MainComment.resourceId }, { "commentId", MainComment.cid },
                        { "type", MainComment.resourceType },
                        { "t", "2" }, { "content", ReplyText.Text }
                    });
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
        AvatarUri = MainComment.AvatarUri;
        if (!Common.Setting.noImage)
        {
            AvatarSource = new BitmapImage();
            AvatarSource.UriSource = AvatarUri;
        }
        ReplyBtn.Visibility = Visibility.Visible;
        FloorCommentsExpander.Visibility = MainComment.IsMainComment ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FloorCommentsExpander_Expanding(Microsoft.UI.Xaml.Controls.Expander sender, Microsoft.UI.Xaml.Controls.ExpanderExpandingEventArgs args)
    {
        LoadFloorComments(false);
    }

    private void FloorCommentsExpander_Collapsed(Microsoft.UI.Xaml.Controls.Expander sender, Microsoft.UI.Xaml.Controls.ExpanderCollapsedEventArgs args)
    {
        floorComments.Clear();
    }
}