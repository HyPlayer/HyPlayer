using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class History : Page
    {

        ObservableCollection<NCSong> Songs;
        public History()
        {
            InitializeComponent();
            Songs = new ObservableCollection<NCSong>();
            HisModeNavView.SelectedItem = SongHis;
        }

        private async void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            switch ((sender.SelectedItem as NavigationViewItem).Name.ToString())
            {
                case "SongHis":
                    Songs.Clear();
                    var Songsl = await HistoryManagement.GetNCSongHistory();
                    var songorder = 0;
                    foreach (var song in Songsl)
                    {
                        song.Order = songorder++;
                        Songs.Add(song);
                    }
                    break;
                case "SongRankWeek":
                    //听歌排行加载部分 - 优先级靠下
                    Songs.Clear();
                    try
                    {
                        await Task.Run(async () =>
                        {
                            JObject ret2 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                            new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "1" } });

                            var weekData = ret2["weekData"].ToArray();

                            for (var i = 0; i < weekData.Length; i++)
                            {
                                var song = NCSong.CreateFromJson(weekData[i]["song"]);
                                song.Order = i;
                                Common.Invoke(() => { Songs.Add(song); });
                            }
                        });

                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
                    }

                    break;
                case "SongRankAll":
                    //听歌排行加载部分 - 优先级靠下
                    Songs.Clear();
                    try
                    {
                        await Task.Run(async () =>
                             {
                                 JObject ret3 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                                 new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "0" } });

                                 var weekData = ret3["allData"].ToArray();
                                 for (var i = 0; i < weekData.Length; i++)
                                 {
                                     var song = NCSong.CreateFromJson(weekData[i]["song"]);
                                     song.Order = i;
                                     Common.Invoke(() => { Songs.Add(song); });

                                 }
                             });

                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
                    }

                    break;
            }
        }
    }
}