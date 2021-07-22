using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleComment : UserControl
    {
        private readonly BitmapImage AvatarSource;
        private readonly Uri AvatarUri;
        private Comment comment;
        private string time;

        public SingleComment(Comment cmt)
        {
            InitializeComponent();
            comment = cmt;
            AvatarUri = comment.AvatarUri;
            AvatarSource = new BitmapImage();
            AvatarSource.UriSource = AvatarUri;
            ReplyBtn.Visibility = Visibility.Visible;
        }

        private async void LoadFloorComments()
        {
            var (IsOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentFloor,
                new Dictionary<string, object>
                {
                    {"parentCommentId", comment.cid}, {"id", comment.resourceId}, {"type", comment.resourceType},
                    {"time", time}
                });
            if (IsOk)
            {
                foreach (var floorcomment in json["data"]["comments"].ToArray())
                    SubCmts.Children.Add(
                        new SingleComment(
                                Comment.CreateFromJson(floorcomment, comment.resourceId, comment.resourceType))
                            {Margin = new Thickness {Left = 5, Right = 5, Top = 5, Bottom = 5}});
                time = json["data"]["time"].ToString();
                if (json["data"]["hasMore"].ToString() == "True")
                    LoadMore.Visibility = Visibility.Visible;
                else LoadMore.Visibility = Visibility.Collapsed;
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(comment.content);
            Clipboard.Clear();
            Clipboard.SetContent(dataPackage);
        }

        private async void Like_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentLike,
                new Dictionary<string, object>
                {
                    {"id", comment.resourceId}, {"cid", comment.cid}, {"type", comment.resourceType},
                    {"t", comment.HasLiked ? "0" : "1"}
                });
            comment.likedCount += comment.HasLiked ? -1 : 1;
            LikeCountTB.Text = comment.likedCount.ToString();
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
                new Dictionary<string, object>
                {
                    {"id", comment.resourceId}, {"t", "0"}, {"type", comment.resourceType}, {"commentId", comment.cid}
                });
            (Parent as StackPanel).Children.Remove(this);
        }

        private void NavToUser_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(Me), comment.uid);
        }

        private async void SendReply_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ReplyText.Text) && Common.Logined)
            {
                try
                {
                    var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
                        new Dictionary<string, object>
                        {
                            {"id", comment.resourceId}, {"commentId", comment.cid}, {"type", comment.resourceType},
                            {"t", "2"}, {"content", ReplyText.Text}
                        });
                    ReplyText.Text = string.Empty;
                    await Task.Delay(1000);
                    LoadFloorComments();
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
            LoadFloorComments();
        }

        private void ReplyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ReplyBtn.IsChecked.Value)
            {
                time = null;
                SubCmtsConainer.Visibility = Visibility.Visible;
                LoadFloorComments();
            }
            else
            {
                SubCmtsConainer.Visibility = Visibility.Collapsed;
            }
        }
    }
}