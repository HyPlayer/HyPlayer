using System;
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
using Newtonsoft.Json.Linq;

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
            else LoadUnLoginedContent();
            HyPlayList.OnLoginDone += LoadLoginedContent;
        }

        private void LoadUnLoginedContent()
        {
            LoadRanklist();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HyPlayList.OnLoginDone -= LoadLoginedContent;
            UnLoginedContent.Children.Clear();
            //DailySongContainer.Children.Clear();
            RankPlayList.Children.Clear();
            //MySongHis.Children.Clear();
            RecommendSongListContainer.Children.Clear();
        }

        private async void LoadLoginedContent()
        {
            UnLoginedContent.Visibility = Visibility.Collapsed;
            LoginedContent.Visibility = Visibility.Visible;
            TbHelloUserName.Text = Common.LoginedUser.name;
            //我们直接Batch吧
            try
            {
                var ret = await Common.ncapi.RequestAsync(
                    CloudMusicApiProviders.Batch,
                    new Dictionary<string, object>
                    {
                        { "/api/toplist", "{}" }
                        //{ "/weapi/v1/discovery/recommend/resource","{}" }   //这个走不了Batch
                    }
                );

                //每日推荐加载部分 - 日推不加载
                /*
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
                */

                //榜单
                RankPlayList.Children.Clear();
                foreach (var bditem in ret["/api/toplist"]["list"])
                    RankPlayList.Children.Add(new PlaylistItem(NCPlayList.CreateFromJson(bditem)));


                //推荐歌单加载部分 - 优先级稍微靠后下
                try
                {
                    var ret1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendResource);

                    RecommendSongListContainer.Children.Clear();
                    foreach (var item in ret1["recommend"])
                        RecommendSongListContainer.Children.Add(new PlaylistItem(NCPlayList.CreateFromJson(item)));
                }
                catch (Exception ex)
                {
                    Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                }
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        public async void LoadRanklist()
        {
            await Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    try
                    {
                        JObject json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Toplist);

                        foreach (JToken PlaylistItemJson in json["list"].ToArray())
                        {
                            NCPlayList ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                            RankList.Children.Add(new PlaylistItem(ncp));
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                    }
                }));
            }));
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
                    try
                    {
                        var jsoon = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                            new Dictionary<string, object> { { "id", Common.MySongLists[0].plid } });
                        var jsona = await Common.ncapi.RequestAsync(
                            CloudMusicApiProviders.PlaymodeIntelligenceList,
                            new Dictionary<string, object>
                            {
                                { "pid", Common.MySongLists[0].plid },
                                { "id", jsoon["playlist"]["trackIds"][0]["id"].ToString() }
                            });

                        var Songs = new List<NCSong>();
                        foreach (var token in jsona["data"])
                        {
                            var ncSong = NCSong.CreateFromJson(token["songInfo"]);
                            Songs.Add(ncSong);
                        }

                        try
                        {
                            HyPlayList.AppendNCSongs(Songs);

                            HyPlayList.SongAppendDone();

                            HyPlayList.SongMoveTo(0);
                        }
                        catch (Exception ex)
                        {
                            Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                    }

                });
            });
        }
    }
}