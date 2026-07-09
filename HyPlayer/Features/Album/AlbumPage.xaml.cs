#region

using AsyncAwaitBestPractices;
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
using HyPlayer.UI.Dialogs;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了"空白页"项模板

namespace HyPlayer.Features.Album;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class AlbumPage : Page
{
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();

    private AlbumPageViewModel ViewModel;
    public AlbumPage()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<AlbumPageViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var albumId = string.Empty;
        switch (e.Parameter)
        {
            case AlbumBase album:
                albumId = album.ActualId;
                break;
            case string:
                albumId = e.Parameter.ToString();
                break;
        }
        ViewModel.LoadAlbumInfo(albumId).SafeFireAndForget();
        ViewModel.LoadAlbumDynamic(albumId).SafeFireAndForget();
    }
    private async void TextBoxAuthor_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (ViewModel.Artists.Count > 1)
            await new ArtistSelectDialog(ViewModel.Artists).ShowAsync();
        else
            _navigation.Navigate(typeof(ArtistPage), ViewModel.Artists[0].ActualId);
    }

    private async void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        await SongContainer.PlayAllAsync();
    }

    private async void AddAll_Click(object sender, RoutedEventArgs e)
    {
        await SongContainer.AddAllToPlaylistAsync();
    }

    private void DownloadAll_Click(object sender, RoutedEventArgs e)
    {
        SongContainer.DownloadAllLoaded();
    }
}
