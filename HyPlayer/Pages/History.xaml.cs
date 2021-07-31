using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using System.Linq;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class History : Page
    {
        public History()
        {
            InitializeComponent();
        }

        private async void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (HistoryPivot.SelectedIndex)
            {
                case 1:
                    SongListHistory.Children.Clear();
                    var PlayListlist = new List<NCPlayList>();
                    PlayListlist = await HistoryManagement.GetSonglistHistory();
                    foreach (var playList in PlayListlist)
                        try
                        {
                            SongListHistory.Children.Add(new SinglePlaylistStack(playList));
                        }
                        catch
                        {
                            //
                        }

                    break;
                case 0:
                    SongHistory.Children.Clear();
                    var Songlist = new List<NCSong>();
                    Songlist = await HistoryManagement.GetNCSongHistory();
                    Common.ListedSongs = Songlist;
                    var songorder = 0;
                    foreach (var song in Songlist)
                        try
                        {
                            SongHistory.Children.Add(new SingleNCSong(song, songorder++, true, true));
                        }
                        catch
                        {
                        }

                    break;
                case 2:
                    //听歌排行加载部分 - 优先级靠下
                    MySongHis.Children.Clear();
                    var (ok2, ret2) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                        new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "1" } });
                    if (ok2)
                    {
                        var weekData = ret2["weekData"].ToArray();
                        MySongHis.Children.Clear();
                        Common.ListedSongs.Clear();
                        for (var i = 0; i < weekData.Length; i++)
                        {
                            var song = NCSong.CreateFromJson(weekData[i]["song"]);
                            Common.ListedSongs.Add(song);
                            MySongHis.Children.Add(new SingleNCSong(song, i, true,
                                true, "最近一周播放 " + weekData[i]["playCount"] + " 次"));
                        }
                    }
                    break;
                case 3:
                    //听歌排行加载部分 - 优先级靠下
                    MySongHisAll.Children.Clear();
                    var (ok3, ret3) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                        new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "0" } });
                    if (ok3)
                    {
                        var weekData = ret3["allData"].ToArray();
                        MySongHisAll.Children.Clear();
                        Common.ListedSongs.Clear();
                        for (var i = 0; i < weekData.Length; i++)
                        {
                            var song = NCSong.CreateFromJson(weekData[i]["song"]);
                            Common.ListedSongs.Add(song);
                            MySongHisAll.Children.Add(new SingleNCSong(song, i, true,
                                true, "共播放 " + weekData[i]["playCount"] + " 次"));
                        }
                    }
                    break;
            }
        }
    }
}