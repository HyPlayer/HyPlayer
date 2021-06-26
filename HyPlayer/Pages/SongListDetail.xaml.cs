using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Input.Spatial;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
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
            InitializeComponent();
        }

        public void LoadSongListDetail()
        {
            ImageRect.ImageSource =
                new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
            TextBoxPLName.Text = playList.name;
            TextBlockDesc.Text = playList.desc;
            TextBoxAuthor.Text = playList.creater.name;
            ToggleButtonLike.IsChecked = playList.subscribed;
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
                        Common.ListedSongs = new List<NCSong>();
                        int idx = page * 500;
                        int i = 0;
                        foreach (JToken jToken in json["songs"])
                        {
                            JObject song = (JObject)jToken;
                            if (string.IsNullOrEmpty(intelsong))
                            {
                                intelsong = song["id"].ToString();
                            }

                            NCSong NCSong = NCSong.CreateFromJson(song);
                            bool canplay =
                                json["privileges"].ToList()[i++]["st"].ToString() == "0";
                            if (canplay)
                            {
                                Common.ListedSongs.Add(NCSong);
                            }

                            SongContainer.Children.Add(new SingleNCSong(NCSong, idx++, canplay, true));
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
                        NCSong NCSong = NCSong.CreateFromJson(song);
                        bool canplay = true;
                        if (canplay)
                        {
                            Common.ListedSongs.Add(NCSong);
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
                        ConnectedAnimation anim = ConnectedAnimationService.GetForCurrentView()
                            .GetAnimation("SongListExpand");
                        ConnectedAnimation anim1 = ConnectedAnimationService.GetForCurrentView()
                            .GetAnimation("SongListExpandAcrylic");
                        anim1?.TryStart(GridPersonalInformation);
                        anim?.TryStart(RectangleImage);
                    }
                    catch
                    {
                    }
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

                            (bool isok, var json) = await Common.ncapi.RequestAsync(
                                CloudMusicApiProviders.PlaylistDetail,
                                new Dictionary<string, object>() { { "id", pid } });
                            if (isok)
                            {
                                playList = NCPlayList.CreateFromJson(json["playlist"]);
                            }
                        }
                    }

                    LoadSongListDetail();
                    LoadSongListItem();
                });
            }));
        }


        private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    HyPlayList.RemoveAllSong();
                    (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                        new Dictionary<string, object>()
                            {{"id", string.Join(',', Common.ListedSongs.Select(t => t.sid))}, {"br", Common.Setting.audioRate}});
                    if (isok)
                    {
                        List<JToken> arr = json["data"].ToList();
                        for (int i = 0; i < Common.ListedSongs.Count; i++)
                        {
                            JToken token = arr.Find(jt => jt["id"].ToString() == Common.ListedSongs[i].sid);
                            if (!token.HasValues)
                            {
                                continue;
                            }

                            NCSong ncSong = Common.ListedSongs[i];

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
                        HistoryManagement.AddSonglistHistory(playList.plid);
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

        private void ButtonHeartBeat_OnClick(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    HyPlayList.RemoveAllSong();
                    (bool isOk, JObject jsona) = await Common.ncapi.RequestAsync(
                        CloudMusicApiProviders.PlaymodeIntelligenceList,
                        new Dictionary<string, object>() { { "pid", playList.plid }, { "id", intelsong } });
                    if (isOk)
                    {
                        List<NCSong> Songs = new List<NCSong>();
                        foreach (JToken token in jsona["data"])
                        {
                            NCSong ncSong = NCSong.CreateFromJson(token["songInfo"]);
                            Songs.Add(ncSong);
                        }

                        (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                            new Dictionary<string, object>()
                                {{"id", string.Join(",", Songs.Select(t => t.sid))}, {"br", Common.Setting.audioRate}});
                        ;
                        if (isok)
                        {
                            List<JToken> arr = json["data"].ToList();
                            for (int i = 0; i < Songs.Count; i++)
                            {
                                JToken token = arr.Find(jt => jt["id"].ToString() == Songs[i].sid);
                                if (!token.HasValues)
                                {
                                    continue;
                                }

                                NCSong ncSong = Songs[i];

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
                    }
                });
            });
        }

        private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(Common.ListedSongs);
        }

        private void LikeBtnClick(object sender, RoutedEventArgs e)
        {
            _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistSubscribe,
                new Dictionary<string, object>() { { "id", playList.plid }, { "t", playList.subscribed ? "0" : "1" } });
            playList.subscribed = !playList.subscribed;
        }

        private void TextBoxAuthor_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(Me), playList.creater.id);
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)TextBlockDesc);
        }

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {

            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDescUpdate, new Dictionary<string, object> { { "id", playList.plid }, { "desc", NewDesc.Text } });
            LoadSongListDetail();

        }
    }
}
