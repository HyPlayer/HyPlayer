using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SongListDetail : Page
    {
        private int page = 0;

        private NCPlayList playList;
        public SongListDetail()
        {
            this.InitializeComponent();
        }

        public void LoadSongListDetail()
        {
            ImageRect.ImageSource = new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
            TextBoxPLName.Text = playList.name;
            TextBlockDesc.Text = playList.desc;
            TextBoxAuthor.Text = playList.creater.name;

        }

        public async void LoadSongListItem()
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                new Dictionary<string, object>() { { "id", playList.plid }, });
            if (isOk)
            {
                int[] trackIds = json["playlist"]["trackIds"].Select(t => (int)t["id"]).Skip(page * 500).Take(500).ToArray();
                if (trackIds.Length >= 500) NextPage.Visibility = Visibility.Visible; else NextPage.Visibility = Visibility.Collapsed;
                (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail, new Dictionary<string, object> { ["ids"] = string.Join(",", trackIds) });
                if (isOk)
                {
                    int idx = page * 500;
                    foreach (var jToken in json["songs"])
                    {
                        var song = (JObject) jToken;
                        NCSong NCSong = new NCSong()
                        {
                            Album = new NCAlbum()
                            {
                                cover = song["al"]["picUrl"].ToString(),
                                id = song["al"]["id"].ToString(),
                                name = song["al"]["name"].ToString()
                            },
                            sid = song["id"].ToString(),
                            songname = song["name"].ToString(),
                            Artist = new List<NCArtist>(),
                            LengthInMilliseconds = Double.Parse(song["dt"].ToString())
                        };
                        song["ar"].ToList().ForEach(t =>
                        {
                            NCSong.Artist.Add(new NCArtist()
                            {
                                id = t["id"].ToString(),
                                name = t["name"].ToString()
                            });
                        });
                        bool canplay =
                            json["privileges"].ToList().Find(x => x["id"].ToString() == song["id"].ToString())[
                                "st"].ToString() == "0";

                        SongContainer.Children.Add(new SingleNCSong(NCSong, idx++, canplay));
                    }
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            playList = (NCPlayList)e.Parameter;
            Task.Run((() =>
            {
                this.Invoke(() =>
                {
                    LoadSongListDetail();
                    LoadSongListItem();
                });
            }));
            var anim = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongListExpand");
            anim?.TryStart(RectangleImage);

        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run((() =>
            {
                this.Invoke((async () =>
                {
                    foreach (UIElement songContainerChild in SongContainer.Children)
                    {
                        if (songContainerChild is SingleNCSong singleNcSong)
                        {
                            await singleNcSong.AppendMe();
                        }
                    }
                }));
            }));

        }


        private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            LoadSongListItem();
        }
    }
}
