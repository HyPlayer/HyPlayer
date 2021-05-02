﻿using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
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
            bool IsFDOn = Common.Setting.FDOption;
            if (IsFDOn)
                this.Background = Application.Current.Resources["SystemControlAcrylicWindowBrush"] as Brush;
            else this.Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
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
                            name = "每日歌曲推荐",
                            desc = "根据你的口味生成，每天6:00更新"
                        };
                        RcmdSongList.Children.Add(new PlaylistItem(dayly));
                        foreach (JToken PlaylistItemJson in json["recommend"].ToArray())
                        {
                            NCPlayList ncp = new NCPlayList()
                            {
                                cover = PlaylistItemJson["picUrl"].ToString(),
                                creater = new NCUser()
                                {
                                    avatar = PlaylistItemJson["creator"]["avatarUrl"].ToString(),
                                    id = PlaylistItemJson["creator"]["userId"].ToString(),
                                    name = PlaylistItemJson["creator"]["nickname"].ToString(),
                                    signature = PlaylistItemJson["creator"]["signature"].ToString()
                                },
                                plid = PlaylistItemJson["id"].ToString(),
                                name = PlaylistItemJson["name"].ToString(),
                                desc = PlaylistItemJson["copywriter"].ToString()
                            };
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
                             NCPlayList ncp = new NCPlayList()
                             {
                                 cover = PlaylistItemJson["coverImgUrl"].ToString(),
                                 creater = new NCUser()
                                 {
                                     avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
                                     id = PlaylistItemJson["userId"].ToString(),
                                     name = PlaylistItemJson["updateFrequency"].ToString(),
                                     signature = "网易云音乐官方帐号"
                                 },
                                 plid = PlaylistItemJson["id"].ToString(),
                                 name = PlaylistItemJson["name"].ToString(),
                                 desc = PlaylistItemJson["description"].ToString()
                             };
                             RankList.Children.Add(new PlaylistItem(ncp));
                         }
                     }
                 }));
             }));
        }
    }
}
