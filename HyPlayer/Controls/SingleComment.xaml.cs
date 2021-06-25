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
        public SingleComment(Comment cmt)
        {
            this.InitializeComponent();
            comment = cmt;
            AvatarUri = comment.AvatarUri;
            AvatarSource = new BitmapImage();
            AvatarSource.UriSource = AvatarUri;
            if(!comment.IsFloorComment)
                LoadFloorComments();
        }
        private async void LoadFloorComments()
        {
            (bool IsOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentFloor, new Dictionary<string, object> { { "parentCommentId", comment.cid }, { "id", comment.resourceId }, { "type", comment.resourceType },{ "limit", 5 } });
            if (IsOk)
            {
                foreach (JToken floorcomment in json["data"]["comments"].ToArray())
                {
                    Comment cmt = new Comment();
                    cmt.resourceId = comment.resourceId;
                    cmt.resourceType = comment.resourceType;
                    cmt.cid = floorcomment["commentId"].ToString();
                    cmt.SendTime =
                        new DateTime((Convert.ToInt64(floorcomment["time"].ToString()) * 10000) + 621355968000000000);
                    cmt.AvatarUri = floorcomment["user"]["avatarUrl"] is null
                        ? new Uri("ms-appx:///Assets/icon.png")
                        : new Uri(floorcomment["user"]["avatarUrl"].ToString() + "?param=" +
                                  StaticSource.PICSIZE_COMMENTUSER_AVATAR);
                    cmt.Nickname = floorcomment["user"]["nickname"] is null
                        ? floorcomment["user"]["userId"].ToString()
                        : floorcomment["user"]["nickname"].ToString();
                    cmt.uid = floorcomment["user"]["userId"].ToString();
                    cmt.content = floorcomment["content"].ToString();
                    cmt.likedCount = floorcomment["likedCount"].ToObject<int>();
                    if (floorcomment["liked"].ToString() == "False")
                        cmt.HasLiked = false;
                    else cmt.HasLiked = true;
                    cmt.IsFloorComment = true;
                    SubCmts.Children.Add(new SingleComment(cmt) { Margin = new Thickness { Left = 5, Right = 5, Top = 5, Bottom = 5 } });
                    System.Diagnostics.Debug.WriteLine(SubCmts.Children);
                }

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
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment, new Dictionary<string, object>() { { "id", comment.resourceId }, { "type", '0' }, { "type", comment.resourceType }, { "commentId", comment.cid } });
            (this.Parent as StackPanel).Children.Remove(this);
        }

        private void NavToUser_Click(object sender, RoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(Me), comment.uid);
        }
    }
}
