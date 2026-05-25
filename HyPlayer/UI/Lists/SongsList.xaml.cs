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
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using HyPlayer.Services.Playback;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.UI.Dialogs;
using System;
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
        "Songs", typeof(ObservableCollection<NCSong>),
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

    private readonly ObservableCollection<NCSong> VisibleSongs = new();

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
        Songs?.CollectionChanged -= Songs_CollectionChanged;
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

    public ObservableCollection<NCSong> Songs
    {
        get => (ObservableCollection<NCSong>)GetValue(SongsProperty);
        set
        {
            try
            {
                Songs?.CollectionChanged -= Songs_CollectionChanged;
                SetValue(SongsProperty, value);
                Songs?.CollectionChanged += Songs_CollectionChanged;
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
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
                if (item is NCSong ncSong)
                {
                    VisibleSongs.Add(ncSong);
                }
        }

        else
        {
            VisibleSongs.Clear();
        }
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

        foreach (NCSong ncsong in SongContainer.SelectedItems)
        {
            if (ncsong.ProviderSong != null)
                _playlist.AppendItem(ncsong.ProviderSong);
            else
                _playlist.AppendNcSong(ncsong);
        }
        if (SongContainer.SelectedItem != null)
        {
            var targetPlayItem =
                _playlist.Items.ToList().Find(t => t.Id == (SongContainer.SelectedItem as NCSong).SongId);
            await _playlist.MoveToAsync(targetPlayItem);
        }
    }

    private void FlyoutItemAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if (!(SongContainer.SelectedItem as NCSong).IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {(SongContainer.SelectedItem as NCSong).SongName} 当前不可用");
            return;
        }

        var selectedSongs = SongContainer.SelectedItems.Cast<NCSong>().ToList();
        var playItems = selectedSongs.All(song => song.ProviderSong != null)
            ? AppendProviderSongs(selectedSongs.Select(song => song.ProviderSong).ToList(), _playlist.NowPlayingIndex + 1)
            : _playlist.AppendNcSongRange(selectedSongs, _playlist.NowPlayingIndex + 1);
        if (_state.ActiveStrategyId == "shn")
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
            var unAvailableSongNames = SongContainer.SelectedItems.Cast<NCSong>().Where(t => !t.IsAvailable)
                .Select(t => t.SongName).ToArray();
            _notification.ShowMessage("歌曲不可用", $"歌曲 {string.Join("/", unAvailableSongNames)} 当前不可用\r已从播放列表中移除");
        }
    }

    private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Type == HyPlayItemType.Radio)
        {
            _navigation.Navigate(typeof(Me), (SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Id);
        }
        else
        {
            if ((SongContainer.SelectedItem as NCSong).Artist.Count > 1)
                await new ArtistSelectDialog((SongContainer.SelectedItem as NCSong).Artist).ShowAsync();
            else
                _navigation.Navigate(typeof(ArtistPage),
                    (SongContainer.SelectedItem as NCSong).Artist.FirstOrDefault().Id);
        }
    }

    private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        if ((SongContainer.SelectedItem as NCSong).Album.Id == "0")
        {
            _notification.ShowMessage("此歌曲无专辑页面");
        }
        else
        {
            _navigation.Navigate(typeof(AlbumPage), (SongContainer.SelectedItem as NCSong).Album);
        }
    }

    private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        _navigation.Navigate(typeof(Comments), CommentTarget.Song((SongContainer.SelectedItem as NCSong).SongId));
    }

    private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (NCSong ncsong in SongContainer.SelectedItems.Cast<NCSong>())
        {
            if (ncsong.ProviderSong != null)
                DownloadManager.AddDownload(ncsong.ProviderSong);
            else
                DownloadManager.AddDownload(ncsong);
        }
    }

    private void BtnMV_Click(object sender, RoutedEventArgs e)
    {
        if (SongContainer.SelectedItems.Count == 0) return;
        _navigation.Navigate(typeof(MVPage), (SongContainer.SelectedItem as NCSong));
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
        VisibleSongs.Remove(SongContainer.SelectedItem as NCSong);
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

    private bool Filter(NCSong ncsong)
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
        if (e.ClickedItem is not NCSong ncSong || IsAddingSongToPlaylist) return;
        if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;

        if (!ncSong.IsAvailable)
        {
            _notification.ShowMessage("歌曲不可用", $"歌曲 {ncSong.SongName} 当前不可用");
            return;
        }

        IsAddingSongToPlaylist = true;
        try
        {
            await _songListQueueBuilder.BuildAndPlayAsync(ncSong, GetEffectiveQueueScope(), VisibleSongs.ToList());
        }
        finally
        {
            IsAddingSongToPlaylist = false;
        }
    }

    private void FilterBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
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

    private SongListQueueScope GetEffectiveQueueScope()
    {
        return IsShowingCompleteSource() ? QueueScope : SongListQueueScope.Visible;
    }

    private List<HyPlayItem> AppendProviderSongs(IReadOnlyList<HyPlayer.PlayCore.Abstraction.Models.SingleItems.SingleSongBase> providerSongs, int position)
    {
        var insertedItems = new List<HyPlayItem>();
        for (var offset = 0; offset < providerSongs.Count; offset++)
        {
            _playlist.AppendItem(providerSongs[offset], position + offset);
            insertedItems.Add(_playlist.Items[position + offset]);
        }

        return insertedItems;
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
}
