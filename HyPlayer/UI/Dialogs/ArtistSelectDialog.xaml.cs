#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Artist;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.UI.Dialogs;

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
        var surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
        if (surfaceCoordinator.IsExpanded)
        {
            surfaceCoordinator.Collapse();
        }

        Hide();
    }
}
