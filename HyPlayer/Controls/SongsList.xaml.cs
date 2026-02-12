#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.Pages;
using ObservableCollections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using WinRT;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls;

public sealed partial class SongsList : UserControl
{
    public static readonly DependencyProperty MultiSelectProperty =
        DependencyProperty.Register("MultiSelect", typeof(bool), typeof(SongsList), new PropertyMetadata(false));


    public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
        "IsSearchEnabled", typeof(bool),
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty SongsProperty = DependencyProperty.Register(
        "Songs", typeof(ObservableList<NCSong>),
        typeof(SongsList),
        new PropertyMetadata(new())
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

    private readonly IWritableSynchronizedView<NCSong, NCSong> VisibleSongsView;
    private readonly ISynchronizedViewList<NCSong> VisibleSongsList;

    public static readonly DependencyProperty CanViewCommentsProperty = DependencyProperty.Register(
        "CanViewComments", typeof(bool), typeof(SongsList), new PropertyMetadata(false));
    //public bool IsManualSelect = true;

    public SongsList()
    {
        InitializeComponent();
        VisibleSongsView = Songs.CreateWritableView(t=>t);
        VisibleSongsList = VisibleSongsView.ToViewList();
        SongContainer.ItemsSource = VisibleSongsView.ToNotifyCollectionChanged().As<IList<NCSong>>();
        HyPlayList.OnPlayItemChange += HyPlayListOnOnPlayItemChange;
        Unloaded += SongsList_Unloaded;
    }

    private void SongsList_Unloaded(object sender, RoutedEventArgs e)
    {
        HyPlayList.OnPlayItemChange -= HyPlayListOnOnPlayItemChange;
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

    public ObservableList<NCSong> Songs
    {
        get => (ObservableList<NCSong>)GetValue(SongsProperty);
        set => SetValue(SongsProperty, value);
    }

    public bool CanViewComments
    {
        get => (bool)GetValue(CanViewCommentsProperty) && Common.Setting.notClearMode;
        set => SetValue(CanViewCommentsProperty, value);
    }

    public string ListSource
    {
        get => (string)GetValue(ListSourceProperty);
        set => SetValue(ListSourceProperty, value);
    }

    public bool IsAddingSongToPlaylist = false;

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

        var idx = VisibleSongsView.ToList().FindIndex(t => t.SongId == playitem.PlayItem.Id);
        if (idx == -1) return;
        _ = Common.Invoke(() =>
        {
            //IsManualSelect = false;
            SongContainer.SelectedIndex = idx;
            //IsManualSelect = true;
        });
    }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        var ncsong = VisibleSongsList[int.Parse((sender?.As<Button>()).Tag.ToString())];
        _ = HyPlayList.AppendNcSong(ncsong);
        HyPlayList.SongMoveTo(HyPlayList.List.Find(t => t.PlayItem.Id == ncsong.SongId));
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
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).SongName} 当前不可用");
            return;
        }

        foreach (NCSong ncsong in SongContainer.SelectedItems)
            _ = HyPlayList.AppendNcSong(ncsong);
        if (SongContainer.SelectedItem != null)
        {
            var targetPlayItem =
                HyPlayList.List.Find(t => t.PlayItem.Id == (SongContainer.SelectedItem as NCSong).SongId);
            HyPlayList.SongMoveTo(targetPlayItem);
        }
    }

    private void FlyoutItemAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).SongName} 当前不可用");
            return;
        }

        var playItems = HyPlayList.AppendNcSongRange(SongContainer.SelectedItems.Cast<NCSong>().ToList(),
            HyPlayList.NowPlaying + 1);
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
            var unAvailableSongNames = SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable)
                .Select(t => t.SongName).ToArray();
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Type == HyPlayItemType.Radio)
        {
            Common.NavigatePage(typeof(Me), (SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Id);
        }
        else
        {
            if ((SongContainer.SelectedItem as NCSong).Artist.Count > 1)
                await new ArtistSelectDialog((SongContainer.SelectedItem as NCSong).Artist).ShowAsync();
            else
                Common.NavigatePage(typeof(ArtistPage),
                    (SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Id);
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong).Album.Id == "0")
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
        Common.NavigatePage(typeof(Comments), "sg" + (SongContainer.SelectedItem as NCSong).SongId);
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
        await new SongListSelect((SongContainer.SelectedItem as NCSong).SongId).ShowAsync();
    }

    private async void Btn_Del_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        var ids = SongContainer.SelectedItems.Cast<NCSong>().Select(t => t.SongId).ToList();
        await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistTracksEditApi,
            new PlaylistTracksEditRequest()
            {
                IdList = ids,
                IsAdd = false,
                PlaylistId = ListSource.Substring(2)
            });
        Songs.Remove(SongContainer.SelectedItem as NCSong);
    }

    private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender?.As<Grid>();
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
        if (e.ClickedItem is not NCSong ncSong || IsAddingSongToPlaylist) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
        bool shiftSong = ncSong.SongId == HyPlayList.NowPlayingItem?.PlayItem?.Id;

        if (!ncSong.IsAvailable)
        {
            Common.AddToTeachingTipLists("歌曲不可用", $"歌曲 {ncSong.SongName} 当前不可用");
            return;
        }

        IsAddingSongToPlaylist = true;
        if (ListSource != null && ListSource != "Content" && Songs.Count == VisibleSongsView.Count)
        {
            if (HyPlayList.PlaySourceId != ListSource.Substring(2) ||
                HyPlayList.List.Count != VisibleSongsView.Count(t => t.IsAvailable))
            {
                // Change Music Source
                HyPlayList.RemoveAllSong(!shiftSong);
                await HyPlayList.AppendNcSource(ListSource);
            }
        }
        /*else if (ListSource == null)
        {
            var ncsong = VisibleSongs[SongContainer.SelectedIndex];
            _ = HyPlayList.AppendNCSong(ncsong);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.Id == ncsong.SongId));
        }*/
        else
        {
            HyPlayList.AppendNcSongs([.. VisibleSongsList], resetPlaying: !shiftSong, currentSongId: ncSong.SongId);
        }

        if (ListSource == "Content")
        {
            HyPlayList.PlaySourceId = "Content";
        }

        if (ListSource?.Substring(0, 2) == "pl" ||
            ListSource?.Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ListSource.Substring(2);

        if (!shiftSong)
        {
            HyPlayList.SongMoveTo(HyPlayList.List.Find(t => t.PlayItem?.Id == ncSong.SongId));
        }
        else
        {
            Common.AddToTeachingTipLists("无感歌单切换", "成功无感切换到歌单 " + ListSource);
            HyPlayList.NowPlaying =
                HyPlayList.List.FindIndex(song => song.PlayItem.Id == ncSong.SongId);
        }

        IsAddingSongToPlaylist = false;
    }

    private void FocusingCurrent_OnClick(object sender, RoutedEventArgs e)
    {
    }

    private void FilterBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(FilterBox.Text))
        {
            VisibleSongsView.AttachFilter((ncsong) =>
            {
                return (ncsong.SongName ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.ArtistString ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.Album?.Name ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.TranslatedName ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.Alias ?? "").ToLower().Contains(FilterBox.Text.ToLower());
            });
        }
        else
        {
            VisibleSongsView.ResetFilter();
        }
    }

    private void ToolbarNavigationView_ItemInvoked(Microsoft.UI.Xaml.Controls.NavigationView sender,
        Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
    {
        var item = args.InvokedItemContainer;
        switch (item.Tag)
        {
            case "FocusingCurrent":
                if (HyPlayList.NowPlayingItem?.PlayItem is null) return;
                var idx = VisibleSongsList.ToList().FindIndex(t => t.SongId == HyPlayList.NowPlayingItem.PlayItem?.Id);
                if (idx == -1) return;
                SongContainer.ScrollIntoView(VisibleSongsList[idx], ScrollIntoViewAlignment.Leading);
                break;
            case "Comments":
                var page = (SongListDetail)((Grid)Parent).Parent;
                Common.NavigatePage(typeof(Comments), "pl" + page.playList.PlaylistId);
                break;
            default:
                break;
        }
    }

    private void FocusingCurrent_OnClicked(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem?.PlayItem is null) return;
        var idx = VisibleSongsList.ToList().FindIndex(t => t.SongId == HyPlayList.NowPlayingItem.PlayItem?.Id);
        if (idx == -1) return;
        SongContainer.ScrollIntoView(VisibleSongsList[idx], ScrollIntoViewAlignment.Leading);
    }
}