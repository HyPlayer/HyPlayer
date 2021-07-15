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
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ArtistPage : Page
    {
        NCArtist artist;
        List<NCSong> songs = new List<NCSong>();

        public ArtistPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var (isOk, res) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistDetail,
                new Dictionary<string, object>() { { "id", (string)e.Parameter } });
            if (isOk)
            {
                artist = NCArtist.CreateFormJson(res["data"]["artist"]);
                if (res["data"]["artist"]["cover"].ToString().StartsWith("http"))
                    ImageRect.ImageSource =
                        new BitmapImage(new Uri(res["data"]["artist"]["cover"].ToString() + "?param=" + StaticSource.PICSIZE_ARTIST_DETAIL_COVER));
                TextBoxArtistName.Text = res["data"]["artist"]["name"].ToString();
                if (res["data"]["artist"]["transNames"].HasValues)
                    TextboxArtistNameTranslated.Text = "译名: " + string.Join(",", res["data"]["artist"]["transNames"].ToObject<string[]>());
                else
                    TextboxArtistNameTranslated.Visibility = Visibility.Collapsed;
                TextBlockDesc.Text = res["data"]["artist"]["briefDesc"].ToString();
                TextBlockInfo.Text = "歌曲数: " + res["data"]["artist"]["musicSize"].ToString() + " | 专辑数: " +
                                     res["data"]["artist"]["albumSize"].ToString() + " | 视频数: " +
                                     res["data"]["artist"]["mvSize"].ToString();
                LoadHotSongs();
            }
        }

        private async void LoadHotSongs()
        {
            var (isok, j1) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistSongs,
                new Dictionary<string, object>() { { "id", artist.id }, { "limit", "10" } });
            if (isok)
            {
                int idx = 0;
                var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                    new Dictionary<string, object> { ["ids"] = string.Join(",", j1["songs"].ToList().Select(t => t["id"])) });
                foreach (JToken jToken in json["songs"])
                {
                    NCSong NCSong = NCSong.CreateFromJson(jToken);
                    bool canplay =
                        json["privileges"].ToList().Find(x => x["id"].ToString() == jToken["id"].ToString())[
                            "st"].ToString() == "0";
                    if (canplay)
                    {
                        songs.Add(NCSong);
                    }

                    HotSongContainer.Children.Add(new SingleNCSong(NCSong, idx++, canplay));
                }
            }
        }

        private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    HyPlayList.RemoveAllSong();
                    (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                        new Dictionary<string, object>() { { "id", string.Join(',', songs.Select(t => t.sid)) }, { "br", Common.Setting.audioRate } });
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
                                Type = HyPlayItemType.Netease,
                                id = ncSong.sid,
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
    }
}
