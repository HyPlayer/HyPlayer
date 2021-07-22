using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Home : Page
    {
        private static List<string> RandomSlogen = new List<string>
        {
            "用音乐开启新的一天吧",
            "戴上耳机 享受新的一天吧"
        };

        public Home()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (Common.Logined)
                LoadLoginedContent();
            HyPlayList.OnLoginDone += LoadLoginedContent;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HyPlayList.OnLoginDone -= LoadLoginedContent;
        }

        private async void LoadLoginedContent()
        {
            UnLoginedContent.Visibility = Visibility.Collapsed;
            LoginedContent.Visibility = Visibility.Visible;
            TbHelloUserName.Text = Common.LoginedUser.name;
            //我们直接Batch吧
            var (isok, ret) = await Common.ncapi.RequestAsync(
                CloudMusicApiProviders.Batch,
                new Dictionary<string, object>
                {
                    {"/api/v3/discovery/recommend/songs", "{}"},
                    {"/api/toplist", "{}"}
                    //{ "/weapi/v1/discovery/recommend/resource","{}" }   //这个走不了Batch
                }
            );
            if (isok)
            {
                //每日推荐加载部分
                var rcmdSongsJson = ret["/api/v3/discovery/recommend/songs"]["data"]["dailySongs"].ToArray();
                Common.ListedSongs.Clear();
                DailySongContainer.Children.Clear();
                var NowSongPanel = new StackPanel();
                for (var c = 0; c < rcmdSongsJson.Length; c++)
                {
                    if (c % 3 == 0)
                    {
                        NowSongPanel = new StackPanel
                        { Orientation = Orientation.Vertical, Height = DailySongContainer.Height, Width = 600 };
                        DailySongContainer.Children.Add(NowSongPanel);
                    }

                    var nownc = NCSong.CreateFromJson(rcmdSongsJson[c]);
                    Common.ListedSongs.Add(nownc);
                    NowSongPanel.Children.Add(new SingleNCSong(nownc, c, true, true,
                        rcmdSongsJson[c]["reason"].ToString()));
                }

                //榜单
                RankPlayList.Children.Clear();
                foreach (var bditem in ret["/api/toplist"]["list"])
                    RankPlayList.Children.Add(new PlaylistItem(NCPlayList.CreateFromJson(bditem)));

                //听歌排行加载部分 - 优先级靠下
                var (ok2, ret2) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                    new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "1" } });
                if (ok2)
                {
                    var weekData = ret2["weekData"].ToArray();
                    MySongHis.Children.Clear();
                    for (var i = 0; i < weekData.Length; i++)
                        MySongHis.Children.Add(new SingleNCSong(NCSong.CreateFromJson(weekData[i]["song"]), i, true,
                            false, "最近一周播放 " + weekData[i]["playCount"] + " 次"));
                }

                //推荐歌单加载部分 - 优先级稍微靠后下
                var (ok1, ret1) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendResource);
                if (ok1)
                {
                    RecommendSongListContainer.Children.Clear();
                    foreach (var item in ret1["recommend"])
                        RecommendSongListContainer.Children.Add(new PlaylistItem(NCPlayList.CreateFromJson(item)));
                }
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            PersonalFM.InitPersonalFM();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    HyPlayList.RemoveAllSong();
                    var (isojbk, jsoon) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                        new Dictionary<string, object> { { "id", Common.MySongLists[0].plid } });
                    var (isOk, jsona) = await Common.ncapi.RequestAsync(
                        CloudMusicApiProviders.PlaymodeIntelligenceList,
                        new Dictionary<string, object>
                        {
                            {"pid", Common.MySongLists[0].plid},
                            {"id", jsoon["playlist"]["trackIds"][0]["id"].ToString()}
                        });
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
    }
}