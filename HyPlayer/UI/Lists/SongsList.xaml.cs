#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.Playlist;
using HyPlayer.Features.User;
using HyPlayer.Features.Video;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using HyPlayer.Services.Playback;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.UI.Dialogs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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

namespace HyPlayer.UI.Lists;

public sealed partial class SongsList : UserControl
{
    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider = Ioc.Default.GetRequiredService<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly ISongListQueueBuilder _songListQueueBuilder = Ioc.Default.GetRequiredService<ISongListQueueBuilder>();
    private readonly WeakEventListener<SongsList, object?, PropertyChangedEventArgs> _stateChangedListener;

    public static readonly DependencyProperty MultiSelectProperty =
        DependencyProperty.Register("MultiSelect", typeof(bool), typeof(SongsList), new PropertyMetadata(false));


    public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
        "IsSearchEnabled", typeof(bool),
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty SongsProperty = DependencyProperty.Register(
        "Songs", typeof(IList),
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty QueueScopeProperty = DependencyProperty.Register(
        "QueueScope", typeof(SongListQueueScope),
        typeof(SongsList),
        new PropertyMetadata(SongListQueueScope.Visible)
    );


    public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
        "IsMySongList", typeof(bool)
        ,
        typeof(SongsList),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty IsCloudStorageListProperty = DependencyProperty.Register(
        "IsCloudStorageList", typeof(bool)
        ,
        typeof(SongsList),
        new PropertyMetadata(false)
    );

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        "ListHeader", typeof(UIElement), typeof(SongsList), new PropertyMetadata(default(UIElement)));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        "Footer", typeof(UIElement), typeof(SongsList), new PropertyMetadata(default(UIElement)));

    private readonly ObservableCollection<SongListItemViewModel> VisibleSongs = new();

    public static readonly DependencyProperty CanViewCommentsProperty = DependencyProperty.Register(
        "CanViewComments", typeof(bool), typeof(SongsList), new PropertyMetadata(false));
    //public bool IsManualSelect = true;

    public SongsList()
    {
        InitializeComponent();
        _stateChangedListener = new WeakEventListener<SongsList, object?, PropertyChangedEventArgs>(this)
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

    private void SongsList_Unloaded(object sender, RoutedEventArgs e)
    {
        _stateChangedListener.Detach();
        if (Songs is INotifyCollectionChanged notifyCollectionChanged)
            notifyCollectionChanged.CollectionChanged -= Songs_CollectionChanged;
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

    public bool IsCloudStorageList
    {
        get => (bool)GetValue(IsCloudStorageListProperty);
        set => SetValue(IsCloudStorageListProperty, value);
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

    public IList Songs
    {
        get => (IList)GetValue(SongsProperty);
        set
        {
            try
            {
                if (Songs is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= Songs_CollectionChanged;
                SetValue(SongsProperty, value);
                if (Songs is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += Songs_CollectionChanged;
                RefreshVisibleSongs();
            }
            catch
            {

            }
        }
    }

    public bool CanViewComments
    {
        get => (bool)GetValue(CanViewCommentsProperty) && _setting.notClearMode;
        set => SetValue(CanViewCommentsProperty, value);
    }

    public SongListQueueScope QueueScope
    {
        get => (SongListQueueScope)GetValue(QueueScopeProperty);
        set => SetValue(QueueScopeProperty, value);
    }

    public bool IsAddingSongToPlaylist = false;

    private void HyPlayListOnOnPlayItemChange(HyPlayItem? playitem)
    {
        if (playitem?.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive || playitem?.PlayItem == null)
        {
            RunOnUIThread(() =>
            {
                if (MultiSelect) return;
                //IsManualSelect = false;
                SongContainer.SelectedIndex = -1;
                //IsManualSelect = true;
            });
            return;
        }

        var idx = VisibleSongs.ToList().FindIndex(t => t.SongId == playitem.Id);
        if (idx == -1) return;
        RunOnUIThread(() =>
        {
            //IsManualSelect = false;
            SongContainer.SelectedIndex = idx;
            //IsManualSelect = true;
        });
    }

    private void Songs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshVisibleSongs();
    }


    private void More_Click(object sender, RoutedEventArgs e)
    {
        Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
    }

    private async void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        if (!selectedSong.IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {selectedSong.SongName} 当前不可用");
            return;
        }

        foreach (var song in GetSelectedRows())
        {
            _playlist.AppendItem(song.ProviderSong ?? song.SourceSong.ToProviderSong());
        }
        if (SongContainer.SelectedItem != null)
        {
            if (selectedSong.ProviderSong is not null)
            {
                await _playlist.MoveToAsync(selectedSong.ProviderSong);
            }
            else
            {
                var targetIndex = _playlist.ProviderQueueSnapshot.ToList()
                    .FindIndex(t => t?.ActualId == selectedSong.SongId);
                if (targetIndex >= 0)
                    await _playlist.MoveToIndexAsync(targetIndex);
            }
        }
    }

    private void FlyoutItemAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        if (!selectedSong.IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {selectedSong.SongName} 当前不可用");
            return;
        }

        var selectedSongs = GetSelectedRows().ToList();
        var playItemIndexes = _playlist.AppendItems(
            selectedSongs.Select(song => song.ProviderSong ?? song.SourceSong.ToProviderSong()).ToList(),
            _playlist.NowPlayingIndex + 1);
        if (_state.ActiveStrategyId == "shn")
        {
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

        if (selectedSongs.Any(t => !t.IsAvailable))
        {
            var unAvailableSongNames = selectedSongs.Where(t => !t.IsAvailable)
                .Select(t => t.SongName).ToArray();
            _notification.ShowMessage("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        if (selectedSong.Artist.FirstOrDefault().Type == HyPlayItemType.Radio)
        {
            _navigation.Navigate(typeof(Me), selectedSong.Artist.FirstOrDefault().Id);
        }
        else
        {
            if (selectedSong.Artist.Count > 1)
                await new ArtistSelectDialog(selectedSong.Artist).ShowAsync();
            else
                _navigation.Navigate(typeof(ArtistPage),
                    selectedSong.Artist.FirstOrDefault().Id);
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        if (selectedSong.Album.Id == "0")
        {
            _notification.ShowMessage("此歌曲无专辑页面");
        }
        else
        {
            _navigation.Navigate(typeof(AlbumPage), selectedSong.Album);
        }
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        _navigation.Navigate(typeof(Comments), CommentTarget.Song(selectedSong.SongId));
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        DownloadManager.AddDownload(GetSelectedRows()
            .Select(song => song.ProviderSong ?? song.SourceSong.ToProviderSong())
            .ToList());
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        _navigation.Navigate(typeof(MVPage), selectedSong.SourceSong);
    }

    private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedRow(out var selectedSong)) return;
        await new SongListSelect(selectedSong.SongId).ShowAsync();
    }

    private async void Btn_Del_Click(object sender, RoutedEventArgs e)
    {
        var selectedSongs = GetSelectedRows().ToList();
        if (selectedSongs.Count == 0) return;
        var ids = selectedSongs.Select(t => t.SongId).ToList();
        if (!IsCloudStorageList)
        {
            if (QueueScope is not { Kind: SongListQueueScopeKind.Playlist, Id: not null }) return;
            var playlist = new NeteasePlaylist { ActualId = QueueScope.Id, Name = string.Empty };
            foreach (var id in ids)
                await playlist.RemoveSongAsync(id);
        }
        else
        {
            var cloudContainer = new NeteaseUserLibrarySubContainer
            {
                ActualId = "cloud",
                Name = "音乐云盘",
                Kind = NeteaseUserLibrarySubContainer.CloudKind
            };
            foreach (var id in ids)
                await cloudContainer.DeleteCloudItemAsync(id);

        }
        if (SongContainer.SelectedItem is SongListItemViewModel row)
            VisibleSongs.Remove(row);
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

    private bool Filter(SongListItemViewModel ncsong)
    {
        if (ncsong == null) return false;
        return (ncsong.SongName ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.ArtistString ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.Album?.Name ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.TranslatedName ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
               (ncsong.Alias ?? "").ToLower().Contains(FilterBox.Text.ToLower());
    }

    private void SongListRoot_Loaded(object sender, RoutedEventArgs e)
    {
        MultiSelect = false;
    }

    private async void SongContainer_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (!TryGetSongRow(e.ClickedItem, out var clickedRow) || IsAddingSongToPlaylist) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;

        if (!clickedRow.IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {clickedRow.SongName} 当前不可用");
            return;
        }

        IsAddingSongToPlaylist = true;
        try
        {
            await _songListQueueBuilder.BuildAndPlayAsync(
                clickedRow.ProviderSong ?? clickedRow.SourceSong.ToProviderSong(),
                GetEffectiveQueueScope(),
                VisibleSongs.Select(song => song.ProviderSong ?? song.SourceSong.ToProviderSong()).ToList());
        }
        finally
        {
            IsAddingSongToPlaylist = false;
        }
    }

    private void FilterBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        RefreshVisibleSongs();
    }

    private SongListQueueScope GetEffectiveQueueScope()
    {
        return IsShowingCompleteSource() ? QueueScope : SongListQueueScope.Visible;
    }

    private bool IsShowingCompleteSource()
    {
        return Songs != null && Songs.Count == VisibleSongs.Count;
    }

    private void ToolbarNavigationView_ItemInvoked(Microsoft.UI.Xaml.Controls.NavigationView sender,
        Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
    {
        var item = args.InvokedItemContainer;
        switch (item.Tag)
        {
            case "FocusingCurrent":
                if (_state.NowPlayingItem?.PlayItem is null) return;
                var idx = VisibleSongs.ToList().FindIndex(t => t.SongId == _state.NowPlayingItem.Id);
                if (idx == -1) return;
                SongContainer.ScrollIntoView(VisibleSongs[idx], ScrollIntoViewAlignment.Leading);
                break;
            case "Comments":
                var page = (SongListDetail)((Grid)Parent).Parent;
                _navigation.Navigate(typeof(Comments), CommentTarget.Playlist(page.ViewModel.PlayList.PlaylistId));
                break;
            default:
                break;
        }
    }
    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(_notification.InvokeOnUIThread(action), "SongsList UI update");
    }

    private void RefreshVisibleSongs()
    {
        VisibleSongs.Clear();
        if (Songs == null) return;

        foreach (var item in Songs)
        {
            if (!TryGetSongRow(item, out var row)) continue;
            if (string.IsNullOrWhiteSpace(FilterBox?.Text) || Filter(row))
                VisibleSongs.Add(row);
        }
    }

    private IReadOnlyList<SongListItemViewModel> GetSelectedRows()
    {
        return [.. SongContainer.SelectedItems.Select(item => TryGetSongRow(item, out var row) ? row : null).Where(row => row != null)];
    }

    private bool TryGetSelectedRow(out SongListItemViewModel song)
    {
        return TryGetSongRow(SongContainer.SelectedItem, out song);
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
