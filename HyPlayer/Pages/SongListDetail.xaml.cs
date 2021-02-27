using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SongListDetail : Page
    {
        private int page = 0;
        private string intelsong = "";

        private NCPlayList playList;

        public SongListDetail()
        {
            this.InitializeComponent();
        }

        public void LoadSongListDetail()
        {
            ImageRect.ImageSource =
                new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
            TextBoxPLName.Text = playList.name;
            TextBlockDesc.Text = playList.desc;
            TextBoxAuthor.Text = playList.creater.name;
        }

        public async void LoadSongListItem()
        {
            if (playList.plid != "-666")
            {
                (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                    new Dictionary<string, object>() { { "id", playList.plid }, });
                if (isOk)
                {
                    int[] trackIds = json["playlist"]["trackIds"].Select(t => (int)t["id"]).Skip(page * 500).Take(500)
                        .ToArray();
                    if (trackIds.Length >= 500)
                    {
                        NextPage.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        NextPage.Visibility = Visibility.Collapsed;
                    }

                    if (json["playlist"]["specialType"].ToString() == "5")
                    {
                        ButtonIntel.Visibility = Visibility.Visible;
                    }

                    (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object> { ["ids"] = string.Join(",", trackIds) });
                    if (isOk)
                    {
                        int idx = page * 500;
                        foreach (JToken jToken in json["songs"])
                        {
                            JObject song = (JObject)jToken;
                            if (string.IsNullOrEmpty(intelsong))
                            {
                                intelsong = song["id"].ToString();
                            }

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
                                LengthInMilliseconds = double.Parse(song["dt"].ToString())
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
            else
            {
                //每日推荐
                ButtonIntel.Visibility = Visibility.Collapsed;
                (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendSongs);
                if (isOk)
                {
                    int idx = 0;
                    foreach (JToken song in json["data"]["dailySongs"])
                    {

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
                            LengthInMilliseconds = double.Parse(song["dt"].ToString())
                        };
                        song["ar"].ToList().ForEach(t =>
                        {
                            NCSong.Artist.Add(new NCArtist()
                            {
                                id = t["id"].ToString(),
                                name = t["name"].ToString()
                            });
                        });
                        bool canplay = true;
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
                Invoke(() =>
                {
                    LoadSongListDetail();
                    LoadSongListItem();
                });
            }));
            ConnectedAnimation anim = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongListExpand");
            anim?.TryStart(RectangleImage);

        }

        public async void Invoke(Action action,
            Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority,
                () => { action(); });
        }

        private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run((() =>
            {
                this.Invoke((async () =>
                {
                    HyPlayList.List.Clear();
                    HyPlayList.RequestSyncPlayList();
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

        private async void ButtonHeartBeat_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.List.Clear();
            HyPlayList.RequestSyncPlayList();
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaymodeIntelligenceList,
                new Dictionary<string, object>() { { "pid", playList.plid }, { "id", intelsong } });
            if (isOk)
            {
                foreach (JToken token in json["data"])
                {
                    NCSong ncSong = new NCSong()
                    {
                        Album = new NCAlbum()
                        {
                            cover = token["songInfo"]["al"]["picUrl"].ToString(),
                            id = token["songInfo"]["al"]["id"].ToString(),
                            name = token["songInfo"]["al"]["name"].ToString()
                        },
                        Artist = new List<NCArtist>(),
                        LengthInMilliseconds = double.Parse(token["songInfo"]["dt"].ToString()),
                        sid = token["songInfo"]["id"].ToString(),
                        songname = token["songInfo"]["name"].ToString()
                    };
                    token["songInfo"]["ar"].ToList().ForEach(t =>
                    {
                        ncSong.Artist.Add(new NCArtist()
                        {
                            id = t["id"].ToString(),
                            name = t["name"].ToString()
                        });
                    });
                    await HyPlayList.AppendNCSong(ncSong);
                }
            }

        }
    }
}
