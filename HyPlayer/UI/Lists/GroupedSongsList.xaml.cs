#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using CommunityToolkit.WinUI.Helpers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using WinRT;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.UI.Lists;

public sealed partial class GroupedSongsList : UserControl
{
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly WeakEventListener<GroupedSongsList, object?, PropertyChangedEventArgs> _stateChangedListener;
    public GroupedSongsListViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<GroupedSongsListViewModel>();

    public static readonly DependencyProperty GroupedSongsProperty = DependencyProperty.Register(
        "GroupedSongs", typeof(CollectionViewSource), typeof(GroupedSongsList),
        new PropertyMetadata(default(CollectionViewSource)));

    public static readonly DependencyProperty QueueScopeProperty = DependencyProperty.Register(
        "QueueScope", typeof(SongListQueueScope),
        typeof(GroupedSongsList),
        new PropertyMetadata(SongListQueueScope.Visible)
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
        _stateChangedListener = new WeakEventListener<GroupedSongsList, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) =>
            {
                if (args.PropertyName == nameof(PlaybackStateService.NowPlayingItem))
                    instance.HyPlayListOnOnPlayItemChange(instance._state.NowPlayingItem);
            },
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
    }

    private void GroupedSongsList_Unloaded(object sender, RoutedEventArgs e)
    {
        _stateChangedListener.Detach();
    }

    public CollectionViewSource GroupedSongs
    {
        get => (CollectionViewSource)GetValue(GroupedSongsProperty);
        set
        {
            SetValue(GroupedSongsProperty, value);
            SongContainer.SelectedIndex = -1;
            HyPlayListOnOnPlayItemChange(ViewModel.NowPlayingItem);
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

    public SongListQueueScope QueueScope
    {
        get => (SongListQueueScope)GetValue(QueueScopeProperty);
        set => SetValue(QueueScopeProperty, value);
    }

    private void HyPlayListOnOnPlayItemChange(HyPlayItem? playitem)
    {
        _ = _notification.InvokeOnUIThread(() =>
        {
            SongContainer.SelectedItem = null;
            if (playitem?.PlayItem == null || GroupedSongs?.Source == null) return;
            foreach (var disc in GroupedSongs.Source as IEnumerable<SongListItemGroup>)
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
        if (TryGetSelectedRow(out var selectedSong))
            await ViewModel.PlayNowAsync(GetSelectedRows(), selectedSong);
    }

    private void FlyoutItemAddToPlayList_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var selectedSong))
            ViewModel.AddToNext(GetSelectedRows(), selectedSong);
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var selectedSong))
            await ViewModel.OpenSingerAsync(selectedSong);
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var selectedSong))
            ViewModel.OpenAlbum(selectedSong);
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var selectedSong))
            ViewModel.OpenComments(selectedSong);
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DownloadSongs(GetSelectedRows());
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var selectedSong))
            ViewModel.OpenMv(selectedSong);
    }

    private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRow(out var selectedSong))
            await ViewModel.CollectAsync(selectedSong);
    }

    private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender?.As<Grid>();
        if (element == null) return;

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
        if (!TryUnwrapSong(e.ClickedItem, out var clickedSong)) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
        var clickedRow = e.ClickedItem is SongListItemViewModel row ? row : SongListItemViewModel.FromNCSong(clickedSong);
        await ViewModel.PlayClickedSongAsync(clickedRow, QueueScope, GetVisibleRows());
    }

    private IReadOnlyList<SongListItemViewModel> GetVisibleRows()
    {
        if (GroupedSongs?.Source is IEnumerable<SongListItemGroup> discs)
        {
            return discs.SelectMany(t => t).ToList();
        }

        return SongContainer.Items.Select(item => TryGetSongRow(item, out var row) ? row : null).Where(row => row != null).ToList();
    }

    private IReadOnlyList<SongListItemViewModel> GetSelectedRows()
    {
        return [.. SongContainer.SelectedItems.Select(item => TryGetSongRow(item, out var row) ? row : null).Where(row => row != null)];
    }

    private bool TryGetSelectedRow(out SongListItemViewModel selectedSong)
    {
        if (TryGetSongRow(SongContainer.SelectedItem, out selectedSong))
        {
            return true;
        }

        selectedSong = null;
        return false;
    }

    private static bool TryUnwrapSong(object item, out NCSong song)
    {
        switch (item)
        {
            case SongListItemViewModel row:
                song = row.SourceSong;
                return true;
            case NCSong ncSong:
                song = ncSong;
                return true;
            default:
                song = null;
                return false;
        }
    }

    private static bool TryGetSongRow(object item, out SongListItemViewModel row)
    {
        switch (item)
        {
            case SongListItemViewModel songRow:
                row = songRow;
                return true;
            case NCSong ncSong:
                row = SongListItemViewModel.FromNCSong(ncSong);
                return true;
            default:
                row = null;
                return false;
        }
    }
}
