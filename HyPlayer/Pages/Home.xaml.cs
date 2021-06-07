using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Home : Page
    {
        public Home()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadRcmdSonglist();
            LoadRanklist();
        }


        public async void LoadRcmdSonglist()
        {
            await Task.Run((() =>
            {
                Common.Invoke((async () =>
                {

                    (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendResource);
                    if (isOk)
                    {
                        NCPlayList dayly = new NCPlayList()
                        {
                            cover = "https://gitee.com/kengwang/HyPlayer/raw/master/HyPlayer/Assets/icon.png",
                            creater = new NCUser()
                            {
                                avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
                                id = "1",
                                name = "网易云音乐",
                                signature = "网易云音乐官方账号 "
                            },
                            plid = "-666",
                            subscribed = false,
                            name = "每日歌曲推荐",
                            desc = "根据你的口味生成，每天6:00更新"
                        };
                        RcmdSongList.Children.Add(new PlaylistItem(dayly));
                        foreach (JToken PlaylistItemJson in json["recommend"].ToArray())
                        {
                            NCPlayList ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                            RcmdSongList.Children.Add(new PlaylistItem(ncp));
                        }
                    }
                }));
            }));
        }

        public async void LoadRanklist()
        {
            await Task.Run((() =>
             {
                 Common.Invoke((async () =>
                 {
                     (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Toplist);
                     if (isOk)
                     {
                         foreach (JToken PlaylistItemJson in json["list"].ToArray())
                         {
                             NCPlayList ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                             RankList.Children.Add(new PlaylistItem(ncp));
                         }
                     }
                 }));
             }));
        }
    }
}
