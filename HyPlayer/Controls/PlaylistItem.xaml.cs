#region

using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class PlaylistItem : UserControl
{
    private readonly NCPlayList playList;
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IUIStateService _uiState = Ioc.Default.GetRequiredService<IUIStateService>();

    public PlaylistItem(NCPlayList playList)
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
        _playlist.Clear();
        await _playlist.AppendPlayListAsync(playList.PlaylistId);
        _playlist.PlaySourceId = $"pl{playList.PlaylistId}";
        await _playlist.MoveNextAsync(true);
    }

    private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
    {
        var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,
            new PlaylistPrivacyRequest()
            {
                Id = playList.PlaylistId
            });
        if (result.IsError)
        {
            _notification.ShowMessage("公开歌单失败", result.Error?.Message ?? "未知错误");
        }
        else
        {
            _notification.ShowMessage("成功公开歌单");
            _ = (_uiState.PageBase as BasePage)?.LoadSongList();
        }
    }

    private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        var result = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,
            new PlaylistDeleteRequest()
            {
                Id = playList.PlaylistId
            });
        if (result.IsError)
        {
            _notification.ShowMessage("删除歌单失败", result.Error?.Message ?? "未知错误");
        }
        else
        {
            _notification.ShowMessage("成功删除");
            _ = (_uiState.PageBase as BasePage)?.LoadSongList();
            _navigation.NavigateRefresh();
        }
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_setting.noImage) ImageContainer.Source = null;
        else
        {
            if (playList.Cover is not null)
                ImageContainerSource.UriSource =
                new Uri(playList.Cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER);
        }

        TextBlockPLName.Text = playList.Name;
        TextBlockPLAuthor.Text = playList.Creator.Name ?? "网易云音乐";
        StoryboardIn.Begin();
    }
}
