using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;

namespace HyPlayer.Controls
{
    public sealed partial class SongListSelect : ContentDialog
    {
        private string sid;
        public SongListSelect(string songid)
        {
            InitializeComponent();
            sid = songid;
            ListViewSongList.Items?.Clear();
            Common.MySongLists.ForEach(t=> ListViewSongList.Items?.Add(t.name));
        }
        
        private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>()
            {
                {"op", "add"},
                {"pid", Common.MySongLists[ListViewSongList.SelectedIndex].plid},
                {"tracks", sid}
            });
            this.Hide();
        }
    }
}