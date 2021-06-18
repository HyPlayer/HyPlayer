using HyPlayer.Classes;
using HyPlayer.Controls;
using Newtonsoft.Json;
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
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var PlayListlist = new List<NCPlayList>();
            var Songlist = new List<NCSong>();
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"] != null)
                PlayListlist = JsonConvert.DeserializeObject<List<NCPlayList>>(ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString());
            if (ApplicationData.Current.LocalSettings.Values["songHistory"] != null)
                Songlist = JsonConvert.DeserializeObject<List<NCSong>>(ApplicationData.Current.LocalSettings.Values["songHistory"].ToString());
            foreach (NCPlayList playList in PlayListlist)
            {
                try
                {
                    SongListHistory.Children.Add(new PlaylistItem(playList));
                }
                catch
                {
                    //
                }
            }
            int songorder = 0;
            foreach (NCSong song in Songlist)
            {
                try
                {
                    SongHistory.Children.Add(new SingleNCSong(song, songorder++));
                }
                catch
                {

                }
            }
        }
    }
}
