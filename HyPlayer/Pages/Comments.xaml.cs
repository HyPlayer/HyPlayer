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
        private int page = 0;
        private NCSong Song;
        public Comments()
        {
            this.InitializeComponent();
        }
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Song = (NCSong)e.Parameter;
            LoadHotComments();
            LoadComments();
        }

        private async void LoadHotComments()
        {
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentMusic, new Dictionary<string, object>() { { "id", Song.sid }, { "offset", page * 20 } });

            if (isOk)
            {
                int index = 0;
                foreach (JToken comment in json["hotComments"].ToArray())
                {
                    Comment cmt = new Comment();

                    cmt.AvatarUri = comment["user"]["avatarUrl"] is null ? new Uri("ms-appx:///Assets/icon.png") : new Uri(comment["user"]["avatarUrl"].ToString());
                    cmt.Nickname = comment["user"]["nickname"] is null ? comment["user"]["userId"].ToString() : comment["user"]["nickname"].ToString();
                    cmt.content = comment["content"].ToString();

                    SingleComment curcomment = new SingleComment(cmt);
                    HotCommentList.Children.Add(curcomment);
                }
            }
        }

        private async void LoadComments()
        {
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentMusic, new Dictionary<string, object>() { { "id", Song.sid }, { "offset", page * 20 } });

            if (isOk)
            {
                CommentList.Children.Clear();
                int index = 0;
                foreach (JToken comment in json["comments"].ToArray())
                {
                    Comment cmt = new Comment();

                    cmt.AvatarUri = comment["user"]["avatarUrl"] is null ? new Uri("ms-appx:///Assets/icon.png") : new Uri(comment["user"]["avatarUrl"].ToString());
                    cmt.Nickname = comment["user"]["nickname"] is null ? comment["user"]["userId"].ToString() : comment["user"]["nickname"].ToString();
                    cmt.content = comment["content"].ToString();

                    SingleComment curcomment = new SingleComment(cmt);
                    CommentList.Children.Add(curcomment);
                }
                if ((json["more"].ToString()) == "True")
                {
                    NextPage.Visibility = Visibility.Visible;
                }
                else
                {
                    NextPage.Visibility = Visibility.Collapsed;
                }
                if (page > 0)
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
            LoadComments();
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            page--;
            LoadComments();
        }

        private async void SendComment_Click(object sender, RoutedEventArgs e)
        {
            if(!String.IsNullOrWhiteSpace(CommentEdit.Text))
                try
                {
                    await Common.ncapi.RequestAsync(CloudMusicApiProviders.Comment, new Dictionary<string, object>() { { "id", Song.sid }, { "type", '0' }, { "t", "1" } ,{ "content", CommentEdit.Text } });
                }
                catch(Exception ex)
                {
                    
                }
            CommentEdit.Text = String.Empty;
            LoadComments();
        }
    }
}
