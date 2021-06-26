using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class History : Page
    {
        public History()
        {
            this.InitializeComponent();
        }

        private async void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (HistoryPivot.SelectedIndex)
            {
                case 0:
                    SongListHistory.Children.Clear();
                    var PlayListlist = new List<NCPlayList>();
                    PlayListlist = await HistoryManagement.GetSonglistHistory();
                    foreach (NCPlayList playList in PlayListlist)
                    {
                        try
                        {
                            SongListHistory.Children.Add(new SinglePlaylistStack(playList));
                        }
                        catch
                        {
                            //
                        }
                    }
                    break;
                case 1:
                    SongHistory.Children.Clear();
                    var Songlist = new List<NCSong>();
                    Songlist = await HistoryManagement.GetNCSongHistory();
                    Common.ListedSongs = Songlist;
                    int songorder = 0;
                    foreach (NCSong song in Songlist)
                    {
                        try
                        {
                            SongHistory.Children.Add(new SingleNCSong(song, songorder++, true, true));
                        }
                        catch
                        {

                        }
                    }
                    break;
            }
        }
    }
}
