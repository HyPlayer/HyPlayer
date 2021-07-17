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
                }
                
            }
            LoadHotComments();
            LoadComments(sortType);
        }


        private void LoadHotComments() => LoadComments(2, HotCommentList);

        private async void LoadComments(int type, StackPanel addingPanel = null)
        {
            if (addingPanel == null)
                addingPanel = CommentList;
            // type 1:按推荐排序,2:按热度排序,3:按时间排序
            if (string.IsNullOrEmpty(resourceid)) return;
            (bool isOk, JObject json) res;

            res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentNew,
            new Dictionary<string, object>() { { "cursor", cursor }, { "id", resourceid }, { "type", resourcetype }, { "pageNo", page }, { "pageSize", 20 }, { "sortType", type } });
            if (res.isOk)
            {
                addingPanel.Children.Clear();
                foreach (JToken comment in res.json["data"]["comments"].ToArray())
                {
                    addingPanel.Children.Add(new SingleComment(Comment.CreateFromJson(comment, resourceid, resourcetype)));
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

        private void SkipPage_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PageSelect.Text, out page))
                LoadComments(sortType);
        }
    }
}
