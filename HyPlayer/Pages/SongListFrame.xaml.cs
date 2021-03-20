using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml.Controls;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SongListFrame : Page
    {
        private readonly string uid;

        public SongListFrame()
        {
            InitializeComponent();
            uid = Common.LoginedUser.id;
            LoadList();
        }

        public async void LoadList()
        {
            try
            {
                (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                    new Dictionary<string, object> { ["uid"] = uid });

                if (isok)
                {
                    foreach (JToken PlaylistItemJson in json["playlist"].ToArray())
                    {
                        NCPlayList ncp = new NCPlayList()
                        {
                            cover = PlaylistItemJson["coverImgUrl"].ToString(),
                            creater = new NCUser()
                            {
                                avatar = PlaylistItemJson["creator"]["avatarUrl"].ToString(),
                                id = PlaylistItemJson["creator"]["userId"].ToString(),
                                name = PlaylistItemJson["creator"]["nickname"].ToString(),
                                signature = PlaylistItemJson["creator"]["signature"].ToString()
                            },
                            plid = PlaylistItemJson["id"].ToString(),
                            name = PlaylistItemJson["name"].ToString(),
                            desc = PlaylistItemJson["description"].ToString()
                        };
                        GridContainer.Children.Add(new PlaylistItem(ncp));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }
    }
}
