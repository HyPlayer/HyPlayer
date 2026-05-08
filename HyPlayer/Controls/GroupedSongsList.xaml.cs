#region

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
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
using WinRT;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls;

public sealed partial class GroupedSongsList : UserControl
{
    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

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

    public GroupedSongsList()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<TrackChangedMessage>(this, (r, m) => ((GroupedSongsList)r).HyPlayListOnOnPlayItemChange(m.Item));
    }

    private void GroupedSongsList_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    public CollectionViewSource GroupedSongs
    {
        get => (CollectionViewSource)GetValue(GroupedSongsProperty);
        set
        {
            SetValue(GroupedSongsProperty, value);
            SongContainer.SelectedIndex = -1;
            HyPlayListOnOnPlayItemChange(_state.NowPlayingItem);
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

    private void HyPlayListOnOnPlayItemChange(HyPlayItem playitem)
    {
        _ = _notification.InvokeOnUIThread(() =>
        {
            SongContainer.SelectedItem = null;
            if (playitem.PlayItem == null || GroupedSongs?.Source == null) return;
            foreach (var disc in GroupedSongs.Source as IEnumerable<DiscSongs>)
            {
                var nowPlayingItem = disc.FirstOrDefault(t => t.SongId == playitem.Id);
                if (nowPlayingItem != null)
                {
                    SongContainer.SelectedItem = nowPlayingItem;
                    break;
                }
            }
        });
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
    }

    private async void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).SongName} 当前不可用");
            return;
        }
        foreach (NCSong ncsong in SongContainer.SelectedItems.Cast<NCSong>())
        {
            _playlist.AppendNcSong(ncsong);
        }

        if (SongContainer.SelectedItem != null)
        {
            var targetPlayItem = _playlist.Items.ToList().Find(t => t.Id == (SongContainer.SelectedItem as NCSong).SongId);
            await _playlist.MoveToAsync(targetPlayItem);
        }
    }

    private void FlyoutItemAddToPlayList_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).SongName} 当前不可用");
            return;
        }
        var playItems = _playlist.AppendNcSongRange([.. SongContainer.SelectedItems.Cast<NCSong>()], _playlist.NowPlayingIndex + 1);
        if (_state.ActiveStrategyId is "shf" or "shn")
        {
            List<int> playItemIndexes = [];
            foreach (var item in playItems)
            {
                var index = _playlist.Items.ToList().IndexOf(item);
                playItemIndexes.Add(index);
            }
            for (int i = 0; i < playItemIndexes.Count; i++)
            {
                var item = playItemIndexes[i];
                var currentIndex = _playlist.ShuffleList.IndexOf(_playlist.NowPlayingIndex);
                if (currentIndex + playItemIndexes.Count >= _playlist.ShuffleList.Count) break; // 如果调不了顺序（歌单剩余空位不足）就算了
                var nextIndex = currentIndex + i + 1;
                var targetIndex = _playlist.ShuffleList.IndexOf(item);
                var t = _playlist.ShuffleList[nextIndex];
                _playlist.ShuffleList[targetIndex] = t;
                _playlist.ShuffleList[nextIndex] = item;
            }
        }
        if (SongContainer.SelectedItems.Cast<NCSong>().Any(t => !t.IsAvailable))
        {
            var unAvailableSongNames = SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable).Select(t => t.SongName).ToArray();
            _notification.ShowMessage("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong)?.Artist[0].Type == HyPlayItemType.Radio)
        {
            Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Me), (SongContainer.SelectedItem as NCSong)?.Artist[0].Id ?? "");
        }
        else
        {
            if (SongContainer.SelectedItem is NCSong { Artist.Count: > 1 })
                await new ArtistSelectDialog((SongContainer.SelectedItem as NCSong)?.Artist).ShowAsync();
            else
                Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(ArtistPage), (SongContainer.SelectedItem as NCSong)?.Artist[0].Id ?? "");
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(AlbumPage), (SongContainer.SelectedItem as NCSong)?.Album.Id ?? "");
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Comments), "sg" + (SongContainer.SelectedItem as NCSong)?.SongId);
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (NCSong ncsong in SongContainer.SelectedItems.Cast<NCSong>())
        {
            DownloadManager.AddDownload(ncsong);
        }
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(MVPage), SongContainer.SelectedItem as NCSong ?? new NCSong());
    }

    private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        await new SongListSelect((SongContainer.SelectedItem as NCSong)?.SongId).ShowAsync();
    }

    private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender?.As<Grid>();
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
        bool shiftSong = ((e.ClickedItem as NCSong).SongId == _state.NowPlayingItem?.Id);

        if (!(e.ClickedItem as NCSong).IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {(e.ClickedItem as NCSong).SongName} 当前不可用");
            return;
        }
        if (_playlist.PlaySourceId != ListSource || SongContainer.Items.Cast<NCSong>().Where(t => t.IsAvailable).Count() != _playlist.Items.Count)
        {
            // Change Music Source
            _playlist.Clear(!shiftSong);
            await _playlist.AppendNcSourceAsync(ListSource);
        }

        if (ListSource[..2] == "pl" ||
            ListSource[..2] == "al")
            _playlist.PlaySourceId = ListSource;
        if (!shiftSong)
            await _playlist.MoveToAsync(_playlist.Items.ToList().Find(t => t?.Id == (e.ClickedItem as NCSong).SongId));
        else
        {
            var targetItem = _playlist.Items.ToList().Find(song => song.Id == ((e.ClickedItem as NCSong).SongId));
            if (targetItem != null) await _playlist.MoveToAsync(targetItem);
        }
    }
}