#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls;

public sealed partial class GroupedSongsList : IDisposable
{
    public static readonly DependencyProperty GroupedSongsProperty = DependencyProperty.Register(
        "GroupedSongs", typeof(CollectionViewSource), typeof(GroupedSongsList),
        new PropertyMetadata(default(CollectionViewSource)));

    public CollectionViewSource GroupedSongs
    {
        get => (CollectionViewSource)GetValue(GroupedSongsProperty);
        set
        {
            SetValue(GroupedSongsProperty, value);
            SongContainer.SelectedIndex = -1;
        }

    }

    public static readonly DependencyProperty ListSourceProperty = DependencyProperty.Register(
        "ListSource", typeof(string),
        typeof(GroupedSongsList),
        new PropertyMetadata(null)
    );


    public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
        "IsMySongList", typeof(bool)
        ,
        typeof(GroupedSongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        "ListHeader", typeof(UIElement), typeof(GroupedSongsList), new PropertyMetadata(default(UIElement)));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        "Footer", typeof(UIElement), typeof(GroupedSongsList), new PropertyMetadata(default(UIElement)));

    public bool IsManualSelect = false;

    public GroupedSongsList()
    {
        InitializeComponent();
        HyPlayList.OnPlayItemChange += HyPlayListOnOnPlayItemChange;
        IndicateNowPlayingItem();
    }

    private async void IndicateNowPlayingItem()
    {
        var tryCount = 5;
        while (--tryCount > 0)
        {
            SongContainer.SelectedItem = null;
            await Task.Delay(TimeSpan.FromSeconds(2));
            try
            {
                HyPlayListOnOnPlayItemChange(HyPlayList.NowPlayingItem);
                break;
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    public UIElement ListHeader
    {
        get => (UIElement)GetValue(ListHeaderProperty);
        set
        {
            HeaderPanel.Padding = new Thickness(0, 0, 0, 25);
            SetValue(ListHeaderProperty, value);
        }
    }

    public UIElement Footer
    {
        get => (UIElement)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }


    public bool IsMySongList
    {
        get => (bool)GetValue(IsMySongListProperty);
        set => SetValue(IsMySongListProperty, value);
    }

    public string ListSource
    {
        get => (string)GetValue(ListSourceProperty);
        set => SetValue(ListSourceProperty, value);
    }

    public void Dispose()
    {
        HyPlayList.OnPlayItemChange -= HyPlayListOnOnPlayItemChange;
    }

    private void HyPlayListOnOnPlayItemChange(HyPlayItem playitem)
    {
        if (playitem?.ItemType == HyPlayItemType.Local || playitem?.PlayItem == null)
        {
            IsManualSelect = false;
            SongContainer.SelectedIndex = -1;
            IsManualSelect = true;
            return;
        }

        var idx = -1;
        foreach (var discSongs in GroupedSongs.Source as IEnumerable<DiscSongs>)
        {
            int index = discSongs.FindIndex(t => t.sid == playitem.PlayItem.Id);
            if (index != -1) idx = index;
        }

        if (idx == -1) return;
        IsManualSelect = false;
        SongContainer.SelectedIndex = idx;
        IsManualSelect = true;
    }


    private async void SongContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsManualSelect) return;
        if (SongContainer.SelectedItem == null || SongContainer.SelectedIndex < 0) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
        if ((SongContainer.SelectedItem as NCSong)?.sid == HyPlayList.NowPlayingItem?.PlayItem?.Id) return;
        if (ListSource != null && ListSource != "content")
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.Player.Pause();
            await HyPlayList.AppendNcSource(ListSource);
            HyPlayList.SongAppendDone();
            if (ListSource.Substring(0, 2) == "pl" ||
                ListSource.Substring(0, 2) == "al")
            {
                HyPlayList.PlaySourceId = ListSource.Substring(2);
            }

            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t =>
                t.PlayItem?.Id == (SongContainer.SelectedItem as NCSong)?.sid));
        }
        //else if (ListSource == null)
        //{
        //    var ncsong = VisibleSongs[SongContainer.SelectedIndex];
        //    _ = HyPlayList.AppendNCSong(ncsong);
        //    HyPlayList.SongAppendDone();
        //    HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.id == ncsong.sid));
        //}
        else
        {
            HyPlayList.AppendNcSongs(GroupedSongs.Source as List<NCSong>);
            HyPlayList.SongAppendDone();
            if (ListSource?.Substring(0, 2) == "pl" ||
                ListSource?.Substring(0, 2) == "al")
            {
                HyPlayList.PlaySourceId = ListSource.Substring(2);
            }

            HyPlayList.SongMoveTo(SongContainer.SelectedIndex);
        }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
    }

    private void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        var origidx = HyPlayList.NowPlaying + 1;
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            _ = HyPlayList.AppendNcSong(ncsong);
        HyPlayList.SongAppendDone();
        if (ListSource.Substring(0, 2) == "pl" ||
            ListSource.Substring(0, 2) == "al")
        {
            HyPlayList.PlaySourceId = ListSource.Substring(2);
        }

        HyPlayList.SongMoveTo(origidx);
    }

    private void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
    {
        _ = HyPlayList.AppendNcSongRange(SongContainer.SelectedItems.Cast<NCSong>().ToList(),
            HyPlayList.NowPlaying + 1);
        HyPlayList.SongAppendDone();
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if ((SongContainer.SelectedItem as NCSong)?.Artist[0].Type == HyPlayItemType.Radio)
        {
            Common.NavigatePage(typeof(Me), (SongContainer.SelectedItem as NCSong)?.Artist[0].id ?? "");
        }
        else
        {
            if ((SongContainer.SelectedItem as NCSong)?.Artist.Count > 1)
                await new ArtistSelectDialog((SongContainer.SelectedItem as NCSong)?.Artist).ShowAsync();
            else
                Common.NavigatePage(typeof(ArtistPage), (SongContainer.SelectedItem as NCSong)?.Artist[0].id ?? "");
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(AlbumPage), (SongContainer.SelectedItem as NCSong)?.Album.id ?? "");
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(Comments), "sg" + (SongContainer.SelectedItem as NCSong)?.sid);
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            DownloadManager.AddDownload(ncsong);
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(MVPage), (SongContainer.SelectedItem as NCSong) ?? new NCSong());
    }

    private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
    {
        await new SongListSelect((SongContainer.SelectedItem as NCSong)?.sid).ShowAsync();
    }

    private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender as Grid;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Single)
        {
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse(element.Tag.ToString());
            IsManualSelect = true;
        }

        SongContainer.ContextFlyout.ShowAt(element,
            new FlyoutShowOptions
            { Position = e?.GetPosition(element) ?? new Point(element?.ActualWidth ?? 0, 80) });
    }

    private void Grid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        IsManualSelect = true;
    }
}