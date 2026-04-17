#region

using HyPlayer.Classes;
using HyPlayer.Pages;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

using HyPlayer.Services.Abstractions;
using CommunityToolkit.Mvvm.DependencyInjection;
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
        artists.ForEach(t => ListViewArtists.Items?.Add(t.Name));
    }


    private void ListViewArtists_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(ArtistPage), aartists[ListViewArtists.SelectedIndex].Id);
        if (Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded)
        {
            if (Ioc.Default.GetRequiredService<Setting>().forceMemoryGarbage)
                Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(BlankPage));
            (Ioc.Default.GetRequiredService<IUIStateService>().BarPlayBar as PlayBar).CollapseExpandedPlayer();
        }

        Hide();
    }
}