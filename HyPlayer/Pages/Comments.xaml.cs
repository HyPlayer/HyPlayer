using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Comments : Page
    {
        private int page = 1;
        private int sortType = 1;
        private NCSong Song;

        public Comments()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Song = (NCSong)e.Parameter;
            SongInfoContainer.Children.Add(new SingleNCSong(Song, 0));
            LoadHotComments();
            LoadComments(sortType);
        }
        private async void LoadHotComments()
        {
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentNew,
                new Dictionary<string, object>() { { "id", Song.sid }, { "type", 0 }, { "pageNo", page }, { "pageSize", 20 }, { "sortType", 2 } });
            if (isOk)
            {
                foreach (JToken comment in json["data"]["comments"].ToArray())
                {
                    Comment cmt = new Comment();
                    cmt.songid = Song.sid;
                    cmt.cid = comment["commentId"].ToString();
                    cmt.AvatarUri = comment["user"]["avatarUrl"] is null ? new Uri("ms-appx:///Assets/icon.png") : new Uri(comment["user"]["avatarUrl"].ToString());
                    cmt.Nickname = comment["user"]["nickname"] is null ? comment["user"]["userId"].ToString() : comment["user"]["nickname"].ToString();
                    cmt.uid = comment["user"]["userId"].ToString();
                    cmt.content = comment["content"].ToString();
                    cmt.likedCount = comment["likedCount"].ToObject<int>();
                    if (comment["liked"].ToString() == "False")
                        cmt.HasLiked = false;
                    else cmt.HasLiked = true;
                    SingleComment curcomment = new SingleComment(cmt);
                    HotCommentList.Children.Add(curcomment);
                }
            }
        }
        private async void LoadComments(int type)
        {
            // type 1:按推荐排序,2:按热度排序,3:按时间排序
            if (string.IsNullOrEmpty(Song.sid)) return;
            (bool isOk, JObject json) res;
            if (type == 1 || type == 2)
            {
                res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentNew,
                new Dictionary<string, object>() { { "id", Song.sid }, { "type", 0 }, { "pageNo", page }, { "pageSize", 20 }, { "sortType", type } });
            }
            else
            {
                res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentMusic, new Dictionary<string, object>() { { "id", Song.sid }, { "offset", page * 20 } });
            }
            if (res.isOk)
            {
                CommentList.Children.Clear();
                foreach (JToken comment in type == 3 ? res.json["comments"].ToArray() : res.json["data"]["comments"].ToArray())
                {
                    Comment cmt = new Comment();
                    cmt.songid = Song.sid;
                    cmt.cid = comment["commentId"].ToString();
                    cmt.SendTime =
                        new DateTime((Convert.ToInt64(comment["time"].ToString()) * 10000) + 621355968000000000);
                    cmt.AvatarUri = comment["user"]["avatarUrl"] is null
                        ? new Uri("ms-appx:///Assets/icon.png")
                        : new Uri(comment["user"]["avatarUrl"].ToString() + "?param=" +
                                  StaticSource.PICSIZE_COMMENTUSER_AVATAR);
                    cmt.Nickname = comment["user"]["nickname"] is null
                        ? comment["user"]["userId"].ToString()
                        : comment["user"]["nickname"].ToString();
                    cmt.uid = comment["user"]["userId"].ToString();
                    cmt.content = comment["content"].ToString();
                    cmt.likedCount = comment["likedCount"].ToObject<int>();
                    if (comment["liked"].ToString() == "False")
                        cmt.HasLiked = false;
                    else cmt.HasLiked = true;
                    CommentList.Children.Add(new SingleComment(cmt));
                }

                if (type == 3 ? (res.json["more"].ToString()) == "True" : (res.json["data"]["hasMore"].ToString()) == "True")
                {
                    NextPage.Visibility = Visibility.Visible;
                }
                else
                {
                    NextPage.Visibility = Visibility.Collapsed;
                }

                if (page > 1)
                {
                    PrevPage.Visibility = Visibility.Visible;
                }
                else
                {
                    PrevPage.Visibility = Visibility.Collapsed;
                }
            }
        }


        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            page++;
            LoadComments(sortType);
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            page--;
            LoadComments(sortType);
        }

        private async void SendComment_Click(object sender, RoutedEventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(CommentEdit.Text) && Common.Logined)
                try
                {
                    await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment,
                        new Dictionary<string, object>()
                            {{"id", Song.sid}, {"type", '0'}, {"t", "1"}, {"content", CommentEdit.Text}});
                    CommentEdit.Text = String.Empty;
                    await System.Threading.Tasks.Task.Delay(1000);
                    LoadComments(1);
                }
                catch (Exception ex)
                {
                    Windows.UI.Popups.MessageDialog dlg = new Windows.UI.Popups.MessageDialog(ex.Message, "出现问题，评论失败");
                    await dlg.ShowAsync();
                }
            else if (String.IsNullOrWhiteSpace(CommentEdit.Text))
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

        private void ComboBoxSortType_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            sortType = ComboBoxSortType.SelectedIndex + 1;
            LoadComments(sortType);
        }
    }
}