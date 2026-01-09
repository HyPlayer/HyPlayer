#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.Pages;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class PlaylistItem : UserControl
{
    private NCPlayList playList;

    public PlaylistItem(NCPlayList playList)
    {
        this.playList = playList;
        InitializeComponent();
    }

    private void UIElement_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        Common.NavigatePage(typeof(SongListDetail), playList, new DrillInNavigationTransitionInfo());
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
        HyPlayList.RemoveAllSong();
        await HyPlayList.AppendPlayList(playList.plid);
        HyPlayList.PlaySourceId = playList.plid;
        HyPlayList.NowPlaying = -1;
        HyPlayList.SongMoveNext();
    }

    private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
    {
        var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistPrivacyApi,
            new PlaylistPrivacyRequest()
            {
                Id = playList.plid
            });
        if (result.IsError)
        {
            Common.AddToTeachingTipLists("公开歌单失败", result.Error?.Message ?? "未知错误");
        }
        else
        {
            Common.AddToTeachingTipLists("成功公开歌单");
            _ = Common.PageBase?.LoadSongList();
        }
    }

    private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistDeleteApi,
            new PlaylistDeleteRequest()
            {
                Id = playList.plid
            });
        if (result.IsError)
        {
            Common.AddToTeachingTipLists("删除歌单失败", result.Error?.Message ?? "未知错误");
        }
        else
        {
            Common.AddToTeachingTipLists("成功删除");
            _ = Common.PageBase?.LoadSongList();
            Common.NavigateRefresh();
        }
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (Common.Setting.noImage) ImageContainer.Source = null;
        else
        {
            if (playList.cover is not null)
                ImageContainerSource.UriSource =
                new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER);
        }

        TextBlockPLName.Text = playList.name;
        TextBlockPLAuthor.Text = playList.creater.name ?? "网易云音乐";
        StoryboardIn.Begin();
    }
}