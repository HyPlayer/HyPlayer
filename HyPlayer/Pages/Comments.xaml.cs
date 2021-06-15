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
        private int resourcetype;
        private string resourceid;
        private string cursor;

        public Comments()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is NCSong ncsong)
            {
                resourcetype = 0;
                resourceid = ncsong.sid;
                SongInfoContainer.Children.Add(new SingleNCSong(ncsong, 0));
            }
            LoadHotComments();
            LoadComments(sortType);
        }


        private async void LoadHotComments()
        {
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentNew,
                new Dictionary<string, object>() { { "id", resourceid }, { "type", resourcetype }, { "pageNo", page }, { "pageSize", 20 }, { "sortType", 2 } });
            if (isOk)
            {
                foreach (JToken comment in json["data"]["comments"].ToArray())
                {
                    Comment cmt = new Comment();
                    cmt.resourceId = resourceid;
                    cmt.resourceType = resourcetype;
                    cmt.cid = comment["commentId"].ToString();
                    cmt.SendTime =
                        new DateTime((Convert.ToInt64(comment["time"].ToString()) * 10000) + 621355968000000000);
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
            if (string.IsNullOrEmpty(resourceid)) return;
            (bool isOk, JObject json) res;

            res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentNew,
            new Dictionary<string, object>() { { "cursor", cursor }, { "id", resourceid }, { "type", resourcetype }, { "pageNo", page }, { "pageSize", 20 }, { "sortType", type } });
            if (res.isOk)
            {
                CommentList.Children.Clear();
                foreach (JToken comment in res.json["data"]["comments"].ToArray())
                {
                    Comment cmt = new Comment();
                    cmt.resourceId = resourceid;
                    cmt.resourceType = resourcetype;
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
                if (type == 3)
                    cursor = res.json["data"]["cursor"].ToString();
                if (res.json["data"]["hasMore"].ToString() == "True")
                {
                    NextPage.IsEnabled = true;
                }
                else
                {
                    NextPage.IsEnabled = false;
                }

                if (page > 1)
                {
                    PrevPage.IsEnabled = true;
                }
                else
                {
                    PrevPage.IsEnabled = false;
                }

                PageIndicator.Text = $"第 {page} 页 / 共 {Math.Ceiling((decimal)res.json["data"]["totalCount"].ToObject<long>() / 20).ToString()} 页";
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
                            {{"id", resourceid}, {"type", resourcetype}, {"t", "1"}, {"content", CommentEdit.Text}});
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
