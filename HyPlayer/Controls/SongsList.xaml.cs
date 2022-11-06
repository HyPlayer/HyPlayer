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

    public bool IsManualSelect = true;

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
            IsManualSelect = false;
            SetValue(MultiSelectProperty, value);
            IsManualSelect = true;
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

    public void Dispose()
    {
        HyPlayList.OnPlayItemChange -= HyPlayListOnOnPlayItemChange;
        VisibleSongs.Clear();
        Songs.Clear();
    }

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
                IsManualSelect = false;
                SongContainer.SelectedIndex = -1;
                IsManualSelect = true;
            });
            return;
        }

        var idx = VisibleSongs.ToList().FindIndex(t => t.sid == playitem.PlayItem.Id);
        if (idx == -1) return;
        _ = Common.Invoke(() =>
        {
            IsManualSelect = false;
            SongContainer.SelectedIndex = idx;
            IsManualSelect = true;
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

    private async void SongContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsManualSelect) return;
        if (SongContainer.SelectedItem == null || SongContainer.SelectedIndex < 0) return;
        var index = SongContainer.SelectedIndex;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
        if (VisibleSongs[index].sid == HyPlayList.NowPlayingItem?.PlayItem?.Id) return;
        if (ListSource != null && ListSource != "content" && Songs.Count == VisibleSongs.Count)
        {
            if (HyPlayList.PlaySourceId != ListSource.Substring(2))
            {
                // Change Music Source
                HyPlayList.RemoveAllSong();
                await HyPlayList.AppendNcSource(ListSource);
                HyPlayList.SongAppendDone();
            }

            if (ListSource.Substring(0, 2) == "pl" ||
                ListSource.Substring(0, 2) == "al")
                HyPlayList.PlaySourceId = ListSource.Substring(2);

            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t =>
                t.PlayItem?.Id == VisibleSongs[index].sid));
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
            HyPlayList.SongAppendDone();
            if (ListSource?.Substring(0, 2) == "pl" ||
                ListSource?.Substring(0, 2) == "al")
                HyPlayList.PlaySourceId = ListSource.Substring(2);

            HyPlayList.SongMoveTo(index);
        }
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        var ncsong = VisibleSongs[int.Parse((sender as Button).Tag.ToString())];
        _ = HyPlayList.AppendNcSong(ncsong);
        HyPlayList.SongAppendDone();
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
        var origidx = HyPlayList.NowPlaying + 1;
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            _ = HyPlayList.AppendNcSong(ncsong);
        HyPlayList.SongAppendDone();
        /*
        if (ListSource?.Substring(0, 2) == "pl" ||
            ListSource?.Substring(0, 2) == "al")
        {
            HyPlayList.PlaySourceId = ListSource.Substring(2);
        }
        */
        var targetPlayItemIndex = HyPlayList.List.FindIndex(t => t.PlayItem.Id == (SongContainer.SelectedItem as NCSong).sid);
        if (HyPlayList.NowPlayType != PlayMode.Shuffled)HyPlayList.SongMoveTo(targetPlayItemIndex);
        else HyPlayList.SongMoveTo(HyPlayList.ShuffleList.Where(t => t==targetPlayItemIndex).FirstOrDefault());
    }

    private void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
    {
        _ = HyPlayList.AppendNcSongRange(SongContainer.SelectedItems.Cast<NCSong>().ToList(),
            HyPlayList.NowPlaying + 1);
        HyPlayList.SongAppendDone();
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (VisibleSongs[SongContainer.SelectedIndex].Artist[0].Type == HyPlayItemType.Radio)
        {
            Common.NavigatePage(typeof(Me), VisibleSongs[SongContainer.SelectedIndex].Artist[0].id);
        }
        else
        {
            if (VisibleSongs[SongContainer.SelectedIndex].Artist.Count > 1)
                await new ArtistSelectDialog(VisibleSongs[SongContainer.SelectedIndex].Artist).ShowAsync();
            else
                Common.NavigatePage(typeof(ArtistPage), VisibleSongs[SongContainer.SelectedIndex].Artist[0].id);
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(AlbumPage), VisibleSongs[SongContainer.SelectedIndex].Album);
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(Comments), "sg" + VisibleSongs[SongContainer.SelectedIndex].sid);
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (NCSong ncsong in SongContainer.SelectedItems)
            DownloadManager.AddDownload(ncsong);
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(MVPage), VisibleSongs[SongContainer.SelectedIndex]);
    }

    private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
    {
        await new SongListSelect(VisibleSongs[SongContainer.SelectedIndex].sid).ShowAsync();
    }

    private async void Btn_Del_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItem is null) return;
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
            IsManualSelect = false;
            SongContainer.SelectedItem=element.DataContext;
            IsManualSelect = true;
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
}