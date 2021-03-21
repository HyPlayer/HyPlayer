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
using Windows.UI.Input.Spatial;
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
        private readonly List<NCSong> songs = new List<NCSong>();
        private NCPlayList playList;

        public SongListDetail()
        {
            InitializeComponent();
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
                            if (canplay)
                            {
                                songs.Add(NCSong);
                            }

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
                        if (canplay)
                        {
                            songs.Add(NCSong);
                        }

                        SongContainer.Children.Add(new SingleNCSong(NCSong, idx++, canplay));
                    }
                }
            }

        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = Task.Run((() =>
            {
                Common.Invoke(() =>
                {
                    try
                    {
                        ConnectedAnimation anim = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongListExpand");
                        ConnectedAnimation anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongListExpandAcrylic");
                        anim1?.TryStart(GridPersonalInformation);
                        anim?.TryStart(RectangleImage);
                    }
                    catch { }

                });
            }));
            await Task.Run((() =>
            {
                Common.Invoke(async () =>
                {
                    if (e.Parameter != null)
                    {
                        if (e.Parameter is NCPlayList)
                            playList = (NCPlayList)e.Parameter;
                        else
                        {
                            string pid = e.Parameter.ToString();

                            (bool isok, var json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                                new Dictionary<string, object>() { { "id", pid } });
                            if (isok)
                            {
                                NCUser user;
                                if (!json["playlist"]["creator"].HasValues)
                                {
                                    user = Common.LoginedUser;
                                }
                                else
                                {
                                    user = new NCUser()
                                    {
                                        avatar = json["playlist"]["creator"]["avatarUrl"].ToString(),
                                        id = json["playlist"]["creator"]["userId"].ToString(),
                                        name = json["playlist"]["creator"]["nickname"].ToString(),
                                        signature = json["playlist"]["creator"]["signature"].ToString()
                                    };
                                }
                                playList = new NCPlayList()
                                {
                                    cover = json["playlist"]["coverImgUrl"].ToString(),
                                    creater = user,
                                    desc = json["playlist"]["description"].ToString(),
                                    name = json["playlist"]["name"].ToString(),
                                    plid = json["playlist"]["id"].ToString()
                                };
                            }


                        }
                    }
                    LoadSongListDetail();
                    LoadSongListItem();
                });
            }));
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
                Invoke((async () =>
                {
                    HyPlayList.RemoveAllSong();
                    (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                        new Dictionary<string, object>() { { "id", string.Join(',', songs.Select(t => t.sid)) } });
                    if (isok)
                    {
                        List<JToken> arr = json["data"].ToList();
                        for (int i = 0; i < songs.Count; i++)
                        {
                            JToken token = arr.Find(jt => jt["id"].ToString() == songs[i].sid);
                            if (!token.HasValues)
                            {
                                continue;
                            }

                            NCSong ncSong = songs[i];

                            string tag = "";
                            if (token["type"].ToString().ToLowerInvariant() == "flac")
                            {
                                tag = "SQ";
                            }
                            else
                            {
                                tag = (token["br"].ToObject<int>() / 1000).ToString() + "k";
                            }
                            NCPlayItem ncp = new NCPlayItem()
                            {
                                tag = tag,
                                Album = ncSong.Album,
                                Artist = ncSong.Artist,
                                subext = token["type"].ToString(),
                                sid = ncSong.sid,
                                songname = ncSong.songname,
                                url = token["url"].ToString(),
                                LengthInMilliseconds = ncSong.LengthInMilliseconds,
                                size = token["size"].ToString(),
                                md5 = token["md5"].ToString()
                            };
                            HyPlayList.AppendNCPlayItem(ncp);
                        }
                        HyPlayList.SongAppendDone();

                        HyPlayList.SongMoveTo(0);
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
            HyPlayList.RemoveAllSong();
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
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(0);
            }

        }
    }
}
