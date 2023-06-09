#region

using HyPlayer.Classes;
using HyPlayer.Pages;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls;

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


    private async void ListViewArtists_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Common.NavigatePage(typeof(ArtistPage), aartists[ListViewArtists.SelectedIndex].id);
        if (Common.isExpanded)
        {
            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            await Common.BarPlayBar.CollapseExpandedPlayer();
        }

        Hide();
    }
}