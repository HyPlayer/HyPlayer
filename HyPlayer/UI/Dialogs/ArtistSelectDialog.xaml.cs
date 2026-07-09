#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Artist;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

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
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(ArtistPage), aartists[ListViewArtists.SelectedIndex].ActualId);
        var surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
        if (surfaceCoordinator.IsExpanded)
        {
            surfaceCoordinator.Collapse();
        }

        Hide();
    }
}
