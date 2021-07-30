using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.Pages;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls
{
    public sealed partial class ArtistSelectDialog : ContentDialog
    {
        private readonly List<NCArtist> aartists;

        public ArtistSelectDialog(List<NCArtist> artists)
        {
            aartists = artists;
            InitializeComponent();
            ListViewArtists.Items?.Clear();
            artists.ForEach(t => ListViewArtists.Items?.Add(t.name));
        }


        private void ListViewArtists_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Common.NavigatePage(typeof(ArtistPage), aartists[ListViewArtists.SelectedIndex].id);
            if (Common.isExpanded)
            {
                Common.NavigatePage(typeof(BlankPage));
                Common.BarPlayBar.ButtonCollapse_OnClick(this, null);
            }
            Hide();
        }
    }
}