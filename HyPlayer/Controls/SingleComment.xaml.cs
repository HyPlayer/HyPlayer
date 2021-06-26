using HyPlayer.Classes;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleComment : UserControl
    {
        private Comment comment;
        private Uri AvatarUri;
        private BitmapImage AvatarSource;
        private string time;
        public SingleComment(Comment cmt)
        {
            this.InitializeComponent();
            comment = cmt;
            AvatarUri = comment.AvatarUri;
            AvatarSource = new BitmapImage();
            AvatarSource.UriSource = AvatarUri;
            ReplyBtn.Visibility = Visibility.Visible;
        }
        private async void LoadFloorComments()
        {
            SubCmts.Children.Clear();
            (bool IsOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentFloor, new Dictionary<string, object> { { "parentCommentId", comment.cid }, { "id", comment.resourceId }, { "type", comment.resourceType }, { "time", time } });
            if (IsOk)
            {
                foreach (JToken floorcomment in json["data"]["comments"].ToArray())
                {
                    SubCmts.Children.Add(new SingleComment(Comment.CreateFromJson(floorcomment, comment.resourceId, comment.resourceType)) { Margin = new Thickness { Left = 5, Right = 5, Top = 5, Bottom = 5 } });
                }
                time = json["data"]["time"].ToString();
                if (json["data"]["hasMore"].ToString() == "True")
                {
                    LoadMore.Visibility = Visibility.Visible;
                }
                else LoadMore.Visibility = Visibility.Collapsed;
            }
        }
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(comment.content);
            Clipboard.Clear();
            Clipboard.SetContent(dataPackage);
        }

        private async void Like_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentLike, new Dictionary<string, object>() { { "id", comment.resourceId }, { "cid", comment.cid }, { "type", comment.resourceType }, { "t", (bool)comment.HasLiked ? "0" : "1" } });
            comment.likedCount += comment.HasLiked ? -1 : 1;
            LikeCountTB.Text = comment.likedCount.ToString();
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment, new Dictionary<string, object>() { { "id", comment.resourceId }, { "t", "0" }, { "type", comment.resourceType }, { "commentId", comment.cid } });
            (this.Parent as StackPanel).Children.Remove(this);
        }

        private void NavToUser_Click(object sender, RoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(Me), comment.uid);
        }

        private async void SendReply_Click(object sender, RoutedEventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(ReplyText.Text) && Common.Logined)
                try
                {
                    (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
                        new Dictionary<string, object>()
                            {{"id", comment.resourceId },{ "commentId",comment.cid}, {"type", comment.resourceType}, {"t", "2"}, {"content", ReplyText.Text}});
                    ReplyText.Text = String.Empty;
                    await System.Threading.Tasks.Task.Delay(1000);
                    LoadFloorComments();
                }
                catch (Exception ex)
                {
                    Windows.UI.Popups.MessageDialog dlg = new Windows.UI.Popups.MessageDialog(ex.Message, "出现问题，评论失败");
                    await dlg.ShowAsync();
                }
            else if (String.IsNullOrWhiteSpace(ReplyText.Text))
            {
                Windows.UI.Popups.MessageDialog dlg = new Windows.UI.Popups.MessageDialog("评论不能为空");
                await dlg.ShowAsync();
            }
            else
            {
                Windows.UI.Popups.MessageDialog dlg = new Windows.UI.Popups.MessageDialog("请先登录");
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
