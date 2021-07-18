using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.Controls;

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
                case 0:
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
                case 1:
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
            }
        }
    }
}