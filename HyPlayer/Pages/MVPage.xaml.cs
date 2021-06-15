using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
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
    public sealed partial class MVPage : Page
    {
        int mvid;
        public MVPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (((int)e.Parameter) == 0) return;
            mvid = (int)e.Parameter;
            HyPlayList.Player.Pause();
            LoadVideo();
            LoadVideoInfo();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            MediaPlayerElement.MediaPlayer.Pause();
        }

        private async void LoadVideo()
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MvUrl, new Dictionary<string, object> { { "id", mvid } });
            if (isOk)
            {
                MediaPlayerElement.Source = MediaSource.CreateFromUri(new Uri(json["data"]["url"].ToString()));
                var mediaPlayer = MediaPlayerElement.MediaPlayer;
                mediaPlayer.Play();
                LoadingControl.IsLoading = false;
            }
        }

        private async void LoadVideoInfo()
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MvDetail, new Dictionary<string, object> { { "mvid", mvid } });
            if (isok)
            {
                TextBoxVideoName.Text = json["data"]["name"].ToString();
                TextBoxSinger.Text = json["data"]["artistName"].ToString();
                TextBoxDesc.Text = json["data"]["desc"].ToString();
                TextBoxOtherInfo.Text = $"发布时间: {json["data"]["publishTime"]}    播放量: {json["data"]["playCount"]}次    收藏量: {json["data"]["subCount"]}次" ;
            }
        }
    }
}
