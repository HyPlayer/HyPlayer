using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
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
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SongListDetail : Page
    {
        private string intelsong = "";
        private int page;
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
            if (playList.creater.id == Common.LoginedUser.id)
                Edit.Visibility = Visibility.Visible;
        }

        public async void LoadSongListItem()
        {
            if (playList.plid != "-666")
            {
                var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                    new Dictionary<string, object> {{"id", playList.plid}});
                if (isOk)
                {
                    var trackIds = json["playlist"]["trackIds"].Select(t => (int) t["id"]).Skip(page * 500).Take(500)
                        .ToArray();
                    if (trackIds.Length >= 500)
                        NextPage.Visibility = Visibility.Visible;
                    else
                        NextPage.Visibility = Visibility.Collapsed;

                    if (json["playlist"]["specialType"].ToString() == "5") ButtonIntel.Visibility = Visibility.Visible;

                    (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object> {["ids"] = string.Join(",", trackIds)});
                    if (isOk)
                    {
                        Common.ListedSongs = new List<NCSong>();
                        var idx = page * 500;
                        var i = 0;
                        foreach (var jToken in json["songs"])
                        {
                            var song = (JObject) jToken;
                            if (string.IsNullOrEmpty(intelsong)) intelsong = song["id"].ToString();

                            var ncSong = NCSong.CreateFromJson(song);
                            var canplay =
                                json["privileges"].ToList()[i++]["st"].ToString() == "0";
                            if (canplay) Common.ListedSongs.Add(ncSong);

                            SongContainer.Children.Add(new SingleNCSong(ncSong, idx++, canplay, true));
                        }
                    }
                }
            }
            else
            {
                //每日推荐
                ButtonIntel.Visibility = Visibility.Collapsed;
                var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendSongs);
                if (isOk)
                {
                    var idx = 0;
                    foreach (var song in json["data"]["dailySongs"])
                    {
                        var ncSong = NCSong.CreateFromJson(song);
                        var canplay = true;
                        if (canplay) Common.ListedSongs.Add(ncSong);

                        SongContainer.Children.Add(new SingleNCSong(ncSong, idx++, canplay));
                    }
                }
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    try
                    {
                        var anim = ConnectedAnimationService.GetForCurrentView()
                            .GetAnimation("SongListExpand");
                        var anim1 = ConnectedAnimationService.GetForCurrentView()
                            .GetAnimation("SongListExpandAcrylic");
                        anim1?.TryStart(GridPersonalInformation);
                        anim?.TryStart(RectangleImage);
                    }
                    catch
                    {
                    }
                });
            });
            await Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    if (e.Parameter != null)
                    {
                        if (e.Parameter is NCPlayList)
                        {
                            playList = (NCPlayList) e.Parameter;
                        }
                        else
                        {
                            var pid = e.Parameter.ToString();

                            var (isok, json) = await Common.ncapi.RequestAsync(
                                CloudMusicApiProviders.PlaylistDetail,
                                new Dictionary<string, object> {{"id", pid}});
                            if (isok) playList = NCPlayList.CreateFromJson(json["playlist"]);
                        }
                    }

                    LoadSongListDetail();
                    LoadSongListItem();
                });
            });
        }


        private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    HyPlayList.RemoveAllSong();
                    var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                        new Dictionary<string, object>
                        {
                            {"id", string.Join(',', Common.ListedSongs.Select(t => t.sid))},
                            {"br", Common.Setting.audioRate}
                        });
                    if (isok)
                    {
                        var arr = json["data"].ToList();
                        for (var i = 0; i < Common.ListedSongs.Count; i++)
                        {
                            var token = arr.Find(jt => jt["id"].ToString() == Common.ListedSongs[i].sid);
                            if (!token.HasValues) continue;

                            var ncSong = Common.ListedSongs[i];

                            var tag = "";
                            if (token["type"].ToString().ToLowerInvariant() == "flac")
                                tag = "SQ";
                            else
                                tag = token["br"].ToObject<int>() / 1000 + "k";

                            var ncp = new NCPlayItem
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

                        HistoryManagement.AddSonglistHistory(playList.plid);
                        HyPlayList.SongAppendDone();

                        HyPlayList.SongMoveTo(0);
                    }
                });
            });
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
                    var (isOk, jsona) = await Common.ncapi.RequestAsync(
                        CloudMusicApiProviders.PlaymodeIntelligenceList,
                        new Dictionary<string, object> {{"pid", playList.plid}, {"id", intelsong}});
                    if (isOk)
                    {
                        var Songs = new List<NCSong>();
                        foreach (var token in jsona["data"])
                        {
                            var ncSong = NCSong.CreateFromJson(token["songInfo"]);
                            Songs.Add(ncSong);
                        }

                        var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                            new Dictionary<string, object>
                                {{"id", string.Join(",", Songs.Select(t => t.sid))}, {"br", Common.Setting.audioRate}});
                        ;
                        if (isok)
                        {
                            var arr = json["data"].ToList();
                            for (var i = 0; i < Songs.Count; i++)
                            {
                                var token = arr.Find(jt => jt["id"].ToString() == Songs[i].sid);
                                if (!token.HasValues) continue;

                                var ncSong = Songs[i];

                                var tag = "";
                                if (token["type"].ToString().ToLowerInvariant() == "flac")
                                    tag = "SQ";
                                else
                                    tag = token["br"].ToObject<int>() / 1000 + "k";

                                var ncp = new NCPlayItem
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
                new Dictionary<string, object> {{"id", playList.plid}, {"t", playList.subscribed ? "0" : "1"}});
            playList.subscribed = !playList.subscribed;
        }

        private void TextBoxAuthor_Tapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            Common.BaseFrame.Navigate(typeof(Me), playList.creater.id);
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout(TextBlockDesc);
        }

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDescUpdate,
                new Dictionary<string, object> {{"id", playList.plid}, {"desc", NewDesc.Text}});
            playList.desc = NewDesc.Text;
            LoadSongListDetail();
        }
    }
}