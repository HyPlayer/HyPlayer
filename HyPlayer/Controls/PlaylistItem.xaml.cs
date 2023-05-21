#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class PlaylistItem : UserControl, IDisposable
{
    private NCPlayList playList;
    private bool disposedValue;

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
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistPrivacy,
                new Dictionary<string, object>
                {
                    { "id", playList.plid }
                });
            json.RemoveAll();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("公开歌单失败", ex.Message);
            return;
        }

        Common.AddToTeachingTipLists("成功公开歌单");
        _ = Common.PageBase.LoadSongList();
    }

    private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDelete,
                new Dictionary<string, object>
                {
                    { "ids", playList.plid }
                });
            Common.AddToTeachingTipLists("成功删除");
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }


        _ = Common.PageBase.LoadSongList();
        Common.NavigateRefresh();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (Common.Setting.noImage) ImageContainer.Source = null;
        else
        {
            ImageContainerSource.UriSource = new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER);
        }
        TextBlockPLName.Text = playList.name;
        TextBlockPLAuthor.Text = playList.creater.name;
        StoryboardIn.Begin();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                playList = null;
                ImageContainer.Source = null;
            }

            disposedValue = true;
        }
    }
    ~PlaylistItem()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}