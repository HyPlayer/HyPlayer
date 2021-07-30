using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MVPage : Page
    {
        private string mvid;
        private string mvquality = "1080";
        private string songid;
        private readonly List<NCMlog> sources = new List<NCMlog>();

        public MVPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var input = e.Parameter as NCSong;
            mvid = input.mvid.ToString();
            songid = input.sid;
            LoadRelateive();
        }

        private void LoadThings()
        {
            HyPlayList.Player.Pause();
            LoadVideo();
            LoadVideoInfo();
            LoadComment();
        }

        private async void LoadRelateive()
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MlogRcmdFeedList,
                new Dictionary<string, object>
                {
                    {"id", mvid},
                    {"songid", songid}
                });
            if (isOk)
            {
                foreach (var jToken in json["data"]["feeds"])
                    sources.Add(NCMlog.CreateFromJson(jToken["resource"]["mlogBaseData"]));

                RelativeList.ItemsSource = sources;
            }
            RelativeList.SelectedIndex = 0;
        }

        private void LoadComment()
        {
            if (Regex.IsMatch(mvid, "^[0-9]*$"))
                CommentFrame.Navigate(typeof(Comments), "mv" + mvid);
            else
                CommentFrame.Navigate(typeof(Comments), "mb" + mvid);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            MediaPlayerElement.MediaPlayer?.Pause();
        }

        private async void LoadVideo()
        {
            if (Regex.IsMatch(mvid, "^[0-9]*$"))
            {
                //纯MV
                var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MvUrl,
                    new Dictionary<string, object> {{"id", mvid}, {"r", mvquality}});
                if (isOk)
                {
                    MediaPlayerElement.Source = MediaSource.CreateFromUri(new Uri(json["data"]["url"].ToString()));
                    var mediaPlayer = MediaPlayerElement.MediaPlayer;
                    mediaPlayer.Play();
                    LoadingControl.IsLoading = false;
                }
            }
            else
            {
                var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MlogUrl,
                    new Dictionary<string, object>
                    {
                        {"id", mvid},
                        {"resolution", mvquality}
                    });
                if (isOk)
                {
                    MediaPlayerElement.Source =
                        MediaSource.CreateFromUri(new Uri(json["data"][mvid]["urlInfo"]["url"].ToString()));
                    var mediaPlayer = MediaPlayerElement.MediaPlayer;
                    mediaPlayer.Play();
                    LoadingControl.IsLoading = false;
                }
            }
        }

        private async void LoadVideoInfo()
        {
            if (Regex.IsMatch(mvid, "^[0-9]*$"))
            {
                var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MvDetail,
                    new Dictionary<string, object> {{"mvid", mvid}});
                if (isok)
                {
                    TextBoxVideoName.Text = json["data"]["name"].ToString();
                    TextBoxSinger.Text = json["data"]["artistName"].ToString();
                    TextBoxDesc.Text = json["data"]["desc"].ToString();
                    TextBoxOtherInfo.Text =
                        $"发布时间: {json["data"]["publishTime"]}    播放量: {json["data"]["playCount"]}次    收藏量: {json["data"]["subCount"]}次";
                    foreach (var br in json["data"]["brs"].ToArray()) VideoQualityBox.Items.Add(br["br"].ToString());
                }
            }
            else
            {
                var mbinfo = sources.Find(t => t.id == mvid);
                TextBoxVideoName.Text = mbinfo.title;
                TextBoxSinger.Text = mbinfo.id;
                TextBoxDesc.Text = mbinfo.description;
                TextBoxOtherInfo.Text = "";
            }
        }

        private void VideoQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            mvquality = VideoQualityBox.SelectedItem.ToString();
            LoadVideo();
        }

        private void RelativeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            mvid = (RelativeList.SelectedItem is NCMlog ? (NCMlog) RelativeList.SelectedItem : default).id;
            LoadThings();
        }
    }
}