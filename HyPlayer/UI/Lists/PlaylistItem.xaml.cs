#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playlist;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Services.Abstractions;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了"用户控件"项模板

namespace HyPlayer.UI.Lists;

public sealed partial class PlaylistItem : UserControl
{
    private readonly NeteasePlaylist playList;
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly IContainerManagementProvidable _containerManagement = Ioc.Default.GetRequiredService<IContainerManagementProvidable>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier = Ioc.Default.GetRequiredService<IPlaylistCollectionChangeNotifier>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    public PlaylistItem(NeteasePlaylist playList)
    {
        this.playList = playList;
        InitializeComponent();
    }

    private void UIElement_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        _navigation.Navigate(typeof(SongListDetail), playList);
    }

    private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        StoryboardOut.Begin();
    }

    private void UIElement_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        StoryboardIn.Begin();
    }

    private void UIElement_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        StoryboardIn.Begin();
    }

    private async void PlayAllBtn_Click(object sender, RoutedEventArgs e)
    {
        //播放全部歌曲
        await _navigator.PlayAsync(new MusicResource.Playlist(playList.ActualId));
    }

    private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _containerManagement.SetContainerPrivacyAsync(playList.ActualId, true);
            _notification.ShowMessage("成功公开歌单");
            _playlistCollectionChangeNotifier.NotifyChanged();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("公开歌单失败", ex.Message);
        }
    }

    private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _containerManagement.DeleteContainerAsync(playList.ActualId);
            _notification.ShowMessage("成功删除");
            _playlistCollectionChangeNotifier.NotifyChanged();
            _navigation.NavigateRefresh();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("删除歌单失败", ex.Message);
        }
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_setting.noImage) ImageContainer.Source = null;
        else
        {
            if (playList.CoverUrl is not null)
                ImageContainerSource.UriSource =
                new Uri(playList.CoverUrl + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER);
        }

        TextBlockPLName.Text = playList.Name;
        TextBlockPLAuthor.Text = playList.Creator?.Name ?? "网易云音乐";
        StoryboardIn.Begin();
    }
}
