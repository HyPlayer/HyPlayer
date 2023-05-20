#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
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
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls;

public sealed partial class SongsList : UserControl, IDisposable
{
    public static readonly DependencyProperty MultiSelectProperty =
        DependencyProperty.Register("MultiSelect", typeof(bool), typeof(SongsList), new PropertyMetadata(false));


    public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
        "IsSearchEnabled", typeof(bool),
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty SongsProperty = DependencyProperty.Register(
        "Songs", typeof(ObservableCollection<NCSong>),
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty ListSourceProperty = DependencyProperty.Register(
        "ListSource", typeof(string),
        typeof(SongsList),
        new PropertyMetadata(null)
    );


    public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
        "IsMySongList", typeof(bool)
        ,
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        "ListHeader", typeof(UIElement), typeof(SongsList), new PropertyMetadata(default(UIElement)));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        "Footer", typeof(UIElement), typeof(SongsList), new PropertyMetadata(default(UIElement)));

    private readonly ObservableCollection<NCSong> VisibleSongs = new();

    //public bool IsManualSelect = true;

    public SongsList()
    {
        InitializeComponent();
        HyPlayList.OnPlayItemChange += HyPlayListOnOnPlayItemChange;
    }

    public bool MultiSelect
    {
        get => (bool)GetValue(MultiSelectProperty);
        set
        {
            /*SongContainer.SelectionMode = (value? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single);*/
            //IsManualSelect = false;
            SetValue(MultiSelectProperty, value);
            //IsManualSelect = true;
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

    public bool IsSearchEnabled
    {
        get => (bool)GetValue(IsSearchEnabledProperty);
        set
        {
            if (value)
                HeaderPanel.Padding = new Thickness(0, 0, 0, 25);
            SetValue(IsSearchEnabledProperty, value);
        }
    }

    public ObservableCollection<NCSong> Songs
    {
        get => (ObservableCollection<NCSong>)GetValue(SongsProperty);
        set
        {
            SetValue(SongsProperty, value);
            try
            {
                Songs.CollectionChanged -= Songs_CollectionChanged;
            }
            catch
            {
            }
            finally
            {
                Songs.CollectionChanged += Songs_CollectionChanged;
            }
        }
    }


    public string ListSource
    {
        get => (string)GetValue(ListSourceProperty);
        set => SetValue(ListSourceProperty, value);
    }

    public bool IsAddingSongToPlaylist = false;
    private bool disposedValue;

    private async Task IndicateNowPlayingItem()
    {
        var tryCount = 5;
        while (--tryCount > 0)
        {
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
        if (playitem?.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive || playitem?.PlayItem == null)
        {
            _ = Common.Invoke(() =>
            {
                if (MultiSelect) return;
                //IsManualSelect = false;
                SongContainer.SelectedIndex = -1;
                //IsManualSelect = true;
            });
            return;
        }

        var idx = VisibleSongs.ToList().FindIndex(t => t.sid == playitem.PlayItem.Id);
        if (idx == -1) return;
        _ = Common.Invoke(() =>
        {
            //IsManualSelect = false;
            SongContainer.SelectedIndex = idx;
            //IsManualSelect = true;
        });
    }

    private void Songs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
                if (item is NCSong ncsong)
                    VisibleSongs.Add(ncsong);
        }

        else
        {
            VisibleSongs.Clear();
        }
    }



    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        var ncsong = VisibleSongs[int.Parse((sender as Button).Tag.ToString())];
        _ = HyPlayList.AppendNcSong(ncsong);
        HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.Id == ncsong.sid));
        if (ListSource.Substring(0, 2) == "pl" ||
            ListSource.Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ListSource.Substring(2);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
    }

    private void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).songname} 当前不可用");
            return;
        }
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            _ = HyPlayList.AppendNcSong(ncsong);
        if (SongContainer.SelectedItem != null)
        {
            var targetPlayItemIndex = HyPlayList.List.FindIndex(t => t.PlayItem.Id == (SongContainer.SelectedItem as NCSong).sid);
            HyPlayList.SongMoveTo(targetPlayItemIndex);
        }
    }

    private void FlyoutItemAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).songname} 当前不可用");
            return;
        }
        var playItems = HyPlayList.AppendNcSongRange(SongContainer.SelectedItems.Cast<NCSong>().ToList(), HyPlayList.NowPlaying + 1);
        if (HyPlayList.NowPlayType == PlayMode.Shuffled)
        {
            List<int> playItemIndexes = new List<int>();
            foreach (var item in playItems)
            {
                var index = HyPlayList.List.IndexOf(item);
                playItemIndexes.Add(index);
            }
            for (int i = 0; i < playItemIndexes.Count; i++)
            {
                var item = playItemIndexes[i];
                var currentIndex = HyPlayList.ShuffleList.IndexOf(HyPlayList.NowPlaying);
                if (currentIndex + playItemIndexes.Count >= HyPlayList.ShuffleList.Count) break; // 如果调不了顺序（歌单剩余空位不足）就算了
                var nextIndex = currentIndex + i + 1;
                var targetIndex = HyPlayList.ShuffleList.IndexOf(item);
                var t = HyPlayList.ShuffleList[nextIndex];
                HyPlayList.ShuffleList[targetIndex] = t;
                HyPlayList.ShuffleList[nextIndex] = item;
            }
        }
        if (SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable).Count() > 0)
        {
            var unAvailableSongNames = SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable).Select(t => t.songname).ToArray();
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Type == HyPlayItemType.Radio)
        {
            Common.NavigatePage(typeof(Me), (SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().id);
        }
        else
        {
            if ((SongContainer.SelectedItem as NCSong).Artist.Count > 1)
                await new ArtistSelectDialog((SongContainer.SelectedItem as NCSong).Artist).ShowAsync();
            else
                Common.NavigatePage(typeof(ArtistPage), (SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().id);
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong).Album.id == "0")
        {
            Common.AddToTeachingTipLists("此歌曲无专辑页面");
        }
        else
        {
            Common.NavigatePage(typeof(AlbumPage), (SongContainer.SelectedItem as NCSong).Album);
        }
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        Common.NavigatePage(typeof(Comments), "sg" + (SongContainer.SelectedItem as NCSong).sid);
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            DownloadManager.AddDownload(ncsong);
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        Common.NavigatePage(typeof(MVPage), (SongContainer.SelectedItem as NCSong));
    }

    private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        await new SongListSelect((SongContainer.SelectedItem as NCSong).sid).ShowAsync();
    }

    private async void Btn_Del_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsCloud)
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks,
            new Dictionary<string, object>
            {
                { "op", "del" },
                { "pid", ListSource.Substring(2, ListSource.Length - 2) },
                { "tracks", (SongContainer.SelectedItem as NCSong).sid }
            });
        else await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserCloudDelete,
            new Dictionary<string, object>
            {
                { "id", (SongContainer.SelectedItem as NCSong).sid },
            });
        VisibleSongs.Remove(SongContainer.SelectedItem as NCSong);
    }

    private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender as Grid;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Single)
        {
            //IsManualSelect = false;
            SongContainer.SelectedItem = element.DataContext;
            //IsManualSelect = true;
        }

        SongContainer.ContextFlyout.ShowAt(element,
            new FlyoutShowOptions
            { Position = e?.GetPosition(element) ?? new Point(element?.ActualWidth ?? 0, 80) });
    }

    public static Brush GetBrush(bool IsAvailable)
    {
        return IsAvailable
            ? (Brush)Application.Current.Resources["DefaultTextForegroundThemeBrush"]
            : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    private void FilterBox_OnTextChanged(object sender, RoutedEventArgs e)
    {
        var vpos = -1;
        for (var b = 0; b < VisibleSongs.Count; b++)
            if (!Songs.Contains(VisibleSongs[b]))
                VisibleSongs.RemoveAt(b);

        for (var i = 0; i < Songs.Count; i++)
            if (string.IsNullOrWhiteSpace(FilterBox.Text) || Filter(Songs[i]))
            {
                vpos++;
                if (!VisibleSongs.Contains(Songs[i])) VisibleSongs.Insert(vpos, Songs[i]);
            }
            else
            {
                VisibleSongs.Remove(Songs[i]);
            }
    }

    private bool Filter(NCSong ncsong)
    {
        if (ncsong == null) return false;
        return (ncsong.songname ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.ArtistString ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.Album?.name ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.transname ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.alias ?? "").ToLower().Contains(FilterBox.Text.ToLower());
    }

    private GridLength GetSearchHeight(bool IsEnabled)
    {
        if (IsEnabled)
            return new GridLength(35);
        return new GridLength(0);
    }

    private void SongListRoot_Loaded(object sender, RoutedEventArgs e)
    {
        MultiSelect = false;
        _ = IndicateNowPlayingItem();
    }

    private async void SongContainer_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem == null || IsAddingSongToPlaylist) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
        bool shiftSong = ((e.ClickedItem as NCSong).sid == HyPlayList.NowPlayingItem?.PlayItem?.Id);

        if (!(e.ClickedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(e.ClickedItem as NCSong).songname} 当前不可用");
            return;
        }
        IsAddingSongToPlaylist = true;
        if (ListSource != null && ListSource != "content" && Songs.Count == VisibleSongs.Count)
        {
            if (HyPlayList.PlaySourceId != ListSource.Substring(2))
            {
                // Change Music Source
                HyPlayList.RemoveAllSong(!shiftSong);
                await HyPlayList.AppendNcSource(ListSource);
            }

            if (ListSource.Substring(0, 2) == "pl" ||
                ListSource.Substring(0, 2) == "al")
                HyPlayList.PlaySourceId = ListSource.Substring(2);
            if (!shiftSong)
                HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem?.Id == (e.ClickedItem as NCSong).sid));
            else
                HyPlayList.NowPlaying =
                    HyPlayList.List.FindIndex(song => song.PlayItem.Id == ((e.ClickedItem as NCSong).sid));
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
            HyPlayList.AppendNcSongs(VisibleSongs);
            if (ListSource?.Substring(0, 2) == "pl" ||
                ListSource?.Substring(0, 2) == "al")
                HyPlayList.PlaySourceId = ListSource.Substring(2);
            if (!shiftSong)
                HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem?.Id == (e.ClickedItem as NCSong).sid));
            else
                HyPlayList.NowPlaying =
                    HyPlayList.List.FindIndex(song => song.PlayItem.Id == ((e.ClickedItem as NCSong).sid));
        }
        IsAddingSongToPlaylist = false;
    }

    private void FocusingCurrent_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem?.PlayItem is null) return;
        var idx = VisibleSongs.ToList().FindIndex(t => t.sid == HyPlayList.NowPlayingItem.PlayItem?.Id);
        if (idx == -1) return;
        SongContainer.ScrollIntoView(VisibleSongs[idx], ScrollIntoViewAlignment.Leading);
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                VisibleSongs.Clear();
            }
            HyPlayList.OnPlayItemChange -= HyPlayListOnOnPlayItemChange;
            Songs.CollectionChanged -= Songs_CollectionChanged;
            disposedValue = true;
        }
    }

    ~SongsList()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
