#region

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Artist;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Dialogs;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了"空白页"项模板

namespace HyPlayer.Features.Album;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class AlbumPage : Page
{
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();

    private readonly AlbumPageViewModel ViewModel;

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