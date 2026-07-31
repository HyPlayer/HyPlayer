#region

using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Artist;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了"内容对话框"项模板

namespace HyPlayer.UI.Dialogs;

public sealed partial class ArtistSelectDialog : ContentDialog
{
    private readonly List<PersonBase> aartists;

    public ArtistSelectDialog(List<PersonBase> artists)
    {
        aartists = artists;
        InitializeComponent();
        ListViewArtists.Items?.Clear();
        artists.ForEach(t => ListViewArtists.Items?.Add(t.Name));
    }


    private void ListViewArtists_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Ioc.Default.GetRequiredService<INavigationService>()
            .Navigate(typeof(ArtistPage), aartists[ListViewArtists.SelectedIndex].ActualId);
        var surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
        if (surfaceCoordinator.IsExpanded) surfaceCoordinator.Collapse();

        Hide();
    }
}