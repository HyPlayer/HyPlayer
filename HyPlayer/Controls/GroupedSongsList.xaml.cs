#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls;

public sealed partial class GroupedSongsList : IDisposable
{
    public static readonly DependencyProperty GroupedSongsProperty = DependencyProperty.Register(
        "GroupedSongs", typeof(CollectionViewSource), typeof(GroupedSongsList),
        new PropertyMetadata(default(CollectionViewSource)));

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
    private bool disposedValue;

    public GroupedSongsList()
    {
        InitializeComponent();
        HyPlayList.OnPlayItemChange += HyPlayListOnOnPlayItemChange;
        _ = IndicateNowPlayingItem();
    }

    public CollectionViewSource GroupedSongs
    {
        get => (CollectionViewSource)GetValue(GroupedSongsProperty);
        set
        {
            SetValue(GroupedSongsProperty, value);
            SongContainer.SelectedIndex = -1;
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

    private async Task IndicateNowPlayingItem()
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

    private void HyPlayListOnOnPlayItemChange(HyPlayItem playitem)
    {
        _ = Common.Invoke(() =>
        {
            if (playitem.PlayItem == null) return;
            if (GroupedSongs.Source == null) return;
            foreach (var disc in GroupedSongs.Source as IEnumerable<DiscSongs>)
            {

                var nowPlayingItem = disc.Where(t => t.sid == playitem.PlayItem.Id).FirstOrDefault();
                if (nowPlayingItem != null)
                {
                    SongContainer.SelectedItem = nowPlayingItem;
                    break;
                }
                else if (SongContainer.SelectedItem != null)
                {
                    SongContainer.SelectedItem = null;
                }
            }
        });
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
    }

    private void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).songname} 当前不可用");
            return;
        }
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            _ = HyPlayList.AppendNcSong(ncsong);
        HyPlayList.SongAppendDone();
        if (SongContainer.SelectedItem != null)
        {
            var targetPlayItemIndex = HyPlayList.List.FindIndex(t => t.PlayItem.Id == (SongContainer.SelectedItem as NCSong).sid);
            HyPlayList.SongMoveTo(targetPlayItemIndex);
        }
    }

    private void FlyoutItemAddToPlayList_Click(object sender, RoutedEventArgs e)
    {
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).songname} 当前不可用");
            return;
        }
        _ = HyPlayList.AppendNcSongRange(SongContainer.SelectedItems.Cast<NCSong>().ToList(),

            HyPlayList.NowPlaying + 1);
        if (SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable).FirstOrDefault() != null)
        {
            var unAvailableSongNames = SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable).Select(t => t.songname).ToArray();
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
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
        Common.NavigatePage(typeof(MVPage), SongContainer.SelectedItem as NCSong ?? new NCSong());
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
            SongContainer.SelectedItem = element.DataContext;
        }

        SongContainer.ContextFlyout.ShowAt(element,
            new FlyoutShowOptions
            { Position = e?.GetPosition(element) ?? new Point(element?.ActualWidth ?? 0, 80) });
    }

    private async void SongContainer_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem == null) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
        bool shiftSong = ((e.ClickedItem as NCSong).sid == HyPlayList.NowPlayingItem?.PlayItem?.Id);

        if (!(e.ClickedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(e.ClickedItem as NCSong).songname} 当前不可用");
            return;
        }
        if (HyPlayList.PlaySourceId != ListSource.Substring(2))
        {
            // Change Music Source
            HyPlayList.RemoveAllSong(!shiftSong);
            await HyPlayList.AppendNcSource(ListSource);
            HyPlayList.SongAppendDone();
        }

        if (ListSource.Substring(0, 2) == "pl" ||
            ListSource.Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ListSource.Substring(2);
        if (!shiftSong)
            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem?.Id == (e.ClickedItem as NCSong).sid));
        else
            HyPlayList.NowPlaying =
                HyPlayList.List.FindIndex(song => song.PlayItem.Id == ((e.ClickedItem as NCSong).sid));
        //else if (ListSource == null)
        //{
        //    var ncsong = VisibleSongs[SongContainer.SelectedIndex];
        //    _ = HyPlayList.AppendNCSong(ncsong);
        //    HyPlayList.SongAppendDone();
        //    HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.id == ncsong.sid));
        //}
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {

            }

            HyPlayList.OnPlayItemChange -= HyPlayListOnOnPlayItemChange;
            disposedValue = true;
        }
    }

    ~GroupedSongsList()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}