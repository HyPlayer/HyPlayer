#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.User;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using HyPlayer.Services.History;
using HyPlayer.Services.Playback;
using HyPlayer.UI.Dialogs;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.System.Profile;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using WinRT;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了"用户控件"项模板

namespace HyPlayer.UI.Playback.PlayBar;

public sealed partial class PlayBar
{
    private SolidColorBrush _playbackAccentBrush = PlaybackThemeSnapshot.Default.AccentBrush;
    private ElementTheme _playbackAccentTheme = ElementTheme.Dark;

    // ---------------------------------------------------------------
    //  ViewModel (resolved from DI; holds all business logic)
    // ---------------------------------------------------------------
    public PlayBarViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<PlayBarViewModel>();

    // ---------------------------------------------------------------
    //  UI-only fields (kept in code-behind)
    // ---------------------------------------------------------------
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IDiagnosticsStateService _diagnostics = Ioc.Default.GetRequiredService<IDiagnosticsStateService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly IPlaybackSurfaceCoordinator _surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly IAppLifecycleStateService _lifecycle = Ioc.Default.GetRequiredService<IAppLifecycleStateService>();
    private WeakEventListener<PlayBar, object?, EventArgs>? _enteredForegroundListener;
    private WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>? _stateChangedListener;
    private WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>? _surfaceStoreChangedListener;
    private WeakEventListener<PlayBar, object?, PlaylistChangedEventArgs>? _playlistChangedListener;
    private WeakEventListener<PlayBar, object?, SongLikeStatusChangedEventArgs>? _songLikeStatusChangedListener;
    private WeakEventListener<PlayBar, object?, EventArgs>? _loginCompletedListener;

    private SolidColorBrush BackgroundElayBrush = new(Colors.Transparent);
    private bool _isSliding = false;
    private TimeSpan StartingTimeSpan = TimeSpan.Zero;
    public ObservableCollection<HyPlayItem> PlayItems => ViewModel.PlaylistItems;

    public SolidColorBrush PlaybackAccentBrush
    {
        get => _playbackAccentBrush;
        private set
        {
            _playbackAccentBrush = value;
            Bindings.Update();
        }
    }

    public ElementTheme PlaybackAccentTheme
    {
        get => _playbackAccentTheme;
        private set
        {
            _playbackAccentTheme = value;
            Bindings.Update();
        }
    }

#nullable enable
    private ManipulationStartedRoutedEventArgs? _slidingEventArgs = null;
#nullable restore

    /*
private Storyboard TbSongNameScrollStoryBoard;
private double lastOffsetX;
DoubleAnimation verticalAnimation;
*/

    public PlayBar()
    {
        InitializeComponent();
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.NowPlayingItem):
                RunOnUIThread(() => LoadPlayingFile(_state.NowPlayingItem));
                break;
            case nameof(PlaybackStateService.CoverStream):
                RefreshPlayBarCover(_state.NowPlayingItem);
                break;
        }
    }

    private void OnSurfaceStorePropertyChanged(PlaybackSurfaceStore store, string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackSurfaceStore.SurfaceMode):
                OnPlaybackSurfaceModeChanged(store.SurfaceMode);
                break;
            case nameof(PlaybackSurfaceStore.Theme):
                ApplyPlaybackTheme(store.Theme);
                break;
        }
    }

    private void OnPlaylistChanged()
    {
        RunOnUIThread(() =>
        {
            if (ViewModel.Items.Count == 0)
                HyPlayListOnOnSongRemoveAll();
            PlayListTitle.Text = ViewModel.GetPlaylistTitle();
        });
    }

    private void OnPlaybackSurfaceModeChanged(PlaybackSurfaceMode mode)
    {
        // Projection from the centralized PlaybackSurfaceStore provides derived visibility booleans.
        // The store is updated by PlaybackShellStateMachine before this message is sent.
        var projection = _surfaceStore.PlayBarProjection;
        var isExpanded = mode == PlaybackSurfaceMode.Expanded;

        RunOnUIThread(() =>
        {
            ButtonExpand.Visibility = projection.ShowExpandButton ? Visibility.Visible : Visibility.Collapsed;
            ButtonCollapse.Visibility = projection.ShowCollapseButton ? Visibility.Visible : Visibility.Collapsed;
            GridSongInfo.Visibility = projection.ShowSongInfo ? Visibility.Visible : Visibility.Collapsed;
            GridSongAdvancedOperation.Visibility = projection.ShowAdvancedOperations ? Visibility.Visible : Visibility.Collapsed;

            if (!isExpanded)
                ApplyPlaybackTheme(PlaybackThemeSnapshot.Default);

            if (!isExpanded)
                StartPreparedCollapseAnimations();

            if (!isExpanded)
                RefreshPlayBarCover(ViewModel.NowPlayingItem);
        });
    }

    private void ApplyPlaybackTheme(PlaybackThemeSnapshot theme)
    {
        PlaybackAccentBrush = theme.AccentBrush;
        PlaybackAccentTheme = theme.IsBright ? ElementTheme.Light : ElementTheme.Dark;
    }

    private void StartPreparedCollapseAnimations()
    {
        if (!_setting.expandAnimation || GridSongInfoContainer.Visibility != Visibility.Visible) return;

        ConnectedAnimation anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
        ConnectedAnimation anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
        ConnectedAnimation anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
        ConnectedAnimation anim4 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongAlbum");
        anim4?.Configuration = new DirectConnectedAnimationConfiguration();
        anim3?.Configuration = new DirectConnectedAnimationConfiguration();
        anim2?.Configuration = new DirectConnectedAnimationConfiguration();
        anim1?.Configuration = new DirectConnectedAnimationConfiguration();
        try
        {
            anim3?.TryStart(TbSingerName);
            anim1?.TryStart(TbSongName);
            anim2?.TryStart(AlbumImage);
            anim4?.TryStart(TbAlbumName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PlayBar collapse connected animation failed: {ex.Message}");
        }
    }

    private void HyPlayListOnOnSongRemoveAll()
    {
        RunOnUIThread(() =>
        {
            PlayItems.Clear();
            PlayListTitle.Text = "播放列表";
        });
    }

    public void SetPlayBarIdleBackground(SolidColorBrush colorBrush)
    {
        var color = colorBrush.Color;
        color.A = 80;
        BackgroundElayBrush = new SolidColorBrush(color);
    }

    public void LoadPlayingFile(HyPlayItem mpi)
    {
        if (mpi == null) return;
        RunOnUIThread(() => ApplicationView.GetForCurrentView().Title =
            $"{mpi.Name} - {mpi.ArtistString}");

        //SliderAudioRate.Value = ViewModel.Volume * 100;

        RunOnUIThread(() =>
        {
            RefreshPlayModeDisplay();

            // 恢复播放音量
            if (ViewModel.NowPlayingItem == null)
            {
                ApplicationView.GetForCurrentView().Title = "";
                return;
            }

            if (ViewModel.NowPlayingItem?.PlayItem == null) return;

            if (_isSliding)
            {
                _slidingEventArgs?.Complete();
                _isSliding = false;
            }

            SliderProgress.Minimum = 0;
            // Maximum/value/current time are provided by PlayBarViewModel x:Bind.

        });
        var isLiked = _auth.LikedSongs.Contains(mpi.Id);
        if (mpi.ItemType != HyPlayItemType.Local && mpi.ItemType != HyPlayItemType.LocalProgressive)
        {
            RunOnUIThread(() =>
            {
                IconLiked.Visibility = isLiked
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                FlyoutLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
                FlyoutLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
            });
            HistoryManagement.AddNCSongHistory(mpi.Id);
        }

        /*
        verticalAnimation.To = TbSongName.ActualWidth - TbSongName.Tb.ActualWidth;
        verticalAnimation.SpeedRatio = 0.1;
        TbSongNameScrollStoryBoard.Stop();
        TbSongNameScrollStoryBoard.Children.Clear();
        TbSongNameScrollStoryBoard.Children.Add(verticalAnimation);
        TbSongNameScrollStoryBoard.Begin();
        */
        RunOnUIThread(TryExpandPendingSurface);
    }

    private void TryExpandPendingSurface()
    {
        if (_surfaceStore.HasPendingExpandedIntent)
            _surfaceCoordinator.Expand();
    }

    private void RefreshPlayModeDisplay()
    {
        if (_state.IsInFm)
        {
            IconPrevious.Glyph = "\uE7E8";
            IconPlayType.Glyph = "\uE107";
            FlyoutPlayRollType.Text = "我不喜欢";
            return;
        }

        IconPrevious.Glyph = "\uF8AC";
        switch (ViewModel.ActiveStrategyId)
        {
            case "shn":
                IconPlayType.Glyph = "\uE14B";
                FlyoutPlayRollType.Text = "随机播放";
                break;
            case "sgl":
                IconPlayType.Glyph = "\uE1CC";
                FlyoutPlayRollType.Text = "单曲循环";
                break;
            default:
                IconPlayType.Glyph = "\uE169";
                FlyoutPlayRollType.Text = "顺序播放";
                break;
        }
    }

    private async void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_player.PlayerCreated || ViewModel.NowPlayingItem == null) return;

        if (_player.PrimaryPlaybackSource == null)
        {
            await _control.LoadAndPlayAsync(ViewModel.NowPlayingItem, setAsPrimary: true, autoPlay: true, removeCurrentSongs: true);
            return;
        }

        ViewModel.TogglePlayPauseCommand.Execute(null);
    }

    private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ViewModel.SetVolumeCommand.Execute(e.NewValue);
    }

    private void BtnMute_OnCllick(object sender, RoutedEventArgs e)
    {
        _player.IsMuted = !_player.IsMuted;
        BtnMuteIcon.Glyph = _player.IsMuted ? "\uE198" : "\uE15D";
        FlyoutBtnMuteIcon.Glyph = _player.IsMuted ? "\uE198" : "\uE15D";
        BtnVolIcon.Glyph = _player.IsMuted ? "\uE198" : "\uE15D";
        //SliderAudioRate.Visibility = _player.IsMuted ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MovePreviousCommand.Execute(null);
    }

    private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveNextCommand.Execute(null);
    }

    private void ListBoxPlayList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxPlayList.SelectedItem != null && ListBoxPlayList.SelectedItem != ViewModel.NowPlayingItem)
        {
            ViewModel.MoveToItemCommand.Execute(ListBoxPlayList.SelectedItem as HyPlayItem);
        }
    }

    private void RequestExpandedPlayer()
    {
        if (!_player.PlayerCreated || ViewModel.NowPlayingItem?.PlayItem?.AudioGraphPlaybackSource == null) return;

        // Prepare ConnectedAnimations from PlayBar elements before coordinator animates ExpandedPlayer
        if (_setting.expandAnimation && GridSongInfoContainer.Visibility == Visibility.Visible)
            try
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TbSongName);
                if (GridSongInfoContainer.Visibility == Visibility.Visible)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", AlbumImage);

                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TbSingerName);
            }
            catch
            {
                //ignore
            }

        // Delegate frame-level operations; UI state updated via PlaybackSurfaceStore
        _surfaceCoordinator.Expand();
    }

    private void ButtonExpand_OnClick(object sender, RoutedEventArgs e)
    {
        RequestExpandedPlayer();
    }

    private void ButtonCollapse_OnClick(object sender, RoutedEventArgs e)
    {
        RequestCompactPlayer();
    }

    private void RequestCompactPlayer()
    {
        // Delegate frame-level operations (animation, visibility, navigation, background, border) to coordinator;
            // the coordinator updates PlaybackSurfaceStore which updates PlayBar UI state.
        _surfaceCoordinator.Collapse();
    }

    private void ButtonCleanAll_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RemoveAllCommand.Execute(null);
    }

    private void ButtonAddLocal_OnClick(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(() => _playlist.PickLocalFileAsync(), "pick local file from play bar");
    }

    private void PlayListRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var item = btn.DataContext as HyPlayItem;
            ViewModel.RemoveItemCommand.Execute(item);
        }
    }

    private void BtnPlayRollType_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_state.IsInFm)
        {
            ViewModel.ChangePlayModeCommand.Execute(null);
            // Update UI icons based on new play mode
            RefreshPlayModeDisplay();
        }
        else
        {
            _taskRunner.Forget(_api.RequestAsync(NeteaseApis.PersonalFmTrashApi,
                new FmTrashRequest
                {
                    Id = ViewModel.NowPlayingItem.Id
                }), "trash personal FM item");
            PersonalFM.LoadNextFMStatic();
        }
        ViewModel.SyncFromState();
    }

    private void BtnLike_OnClick(object sender, RoutedEventArgs e)
    {
        _auth.LikeSong();
    }

    private async void TbSingerName_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (ViewModel.NowPlayingItem.Artist[0].Type == HyPlayItemType.Radio)
                {
                    _navigation.Navigate(typeof(Me), ViewModel.NowPlayingItem.Artist[0].Id);
                }
                else
                {
                    if (ViewModel.NowPlayingItem.Artist.Count > 1)
                        await new ArtistSelectDialog(ViewModel.NowPlayingItem.Artist).ShowAsync();
                    else
                        _navigation.Navigate(typeof(ArtistPage),
                            ViewModel.NowPlayingItem.Artist[0].Id);
                }

                //RequestCompactPlayer();
            }
        }
        catch
        {
        }
    }

    private void TbAlbumName_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (ViewModel.NowPlayingItem.Artist[0].Type == HyPlayItemType.Radio)
                {
                    _navigation.Navigate(typeof(Me), ViewModel.NowPlayingItem.Artist[0].Id);
                }
                else
                {
                    if (ViewModel.NowPlayingItem.Album.Id != "0")
                        _navigation.Navigate(typeof(AlbumPage),
                            ViewModel.NowPlayingItem.Album.Id);
                }
            }
        }
        catch
        {
        }
    }

    private async void Btn_Sub_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            await new SongListSelect(ViewModel.NowPlayingItem.Id).ShowAsync();
    }

    private void Btn_Down_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingItem.ItemType is HyPlayItemType.Netease or HyPlayItemType.Radio)
        {
            DownloadManager.AddDownload(ViewModel.NowPlayingItem.ToNCSong());
        }
    }

    private void Btn_Comment_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            _navigation.Navigate(typeof(Comments), CommentTarget.Song(ViewModel.NowPlayingItem.Id));
        else
            _navigation.Navigate(typeof(Comments), CommentTarget.RadioProgram(ViewModel.NowPlayingItem.Album.Alias));
        RequestCompactPlayer();
    }

    private void Btn_Share_OnClick(object sender, RoutedEventArgs e)
    {
        // NOTE: 分享电台节目功能尚未实现
        if (ViewModel.NowPlayingItem.ItemType != HyPlayItemType.Netease) return;

        //展示系统的共享ui
        DataTransferManager.ShowShareUI();
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // 当前未打开歌词
        Bindings.Update();
        var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
        if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri,
                "306200B4771A6.217957860C1A5_mb3g82vhcggpy") != LaunchQuerySupportStatus.Available)
        {
            var dlg = new ContentDialog
            {
                Title = "关于桌面歌词",
                Content =
                    "目前 HyPlayer 已经适配「热词」，我们推荐使用「热词」来获得真正的桌面歌词体验，可以前往 Microsoft 商店安装 「热词」",
                CloseButtonText = "否",
                PrimaryButtonText = "安装 「热词」"
            };

            var res = await dlg.ShowAsync(ContentDialogPlacement.Popup);
            if (res == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9"));
                return;
            }

            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri, new LauncherOptions
            {
                FallbackUri = new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9")
            });
            Bindings.Update();
        }
        catch
        {

        }
    }

    private void BtnPlayStateChange_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        //BtnPlayStateChange_OnClick(sender, e);
    }

    private void ImageContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ButtonExpand_OnClick(sender, e);
    }

    private void ButtonPlayList_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingIndex >= 0 && ViewModel.NowPlayingIndex < PlayItems.Count)
        {
            ListBoxPlayList.ScrollIntoView(PlayItems[ViewModel.GetTargetingIndex()]);
        }
    }

    private void ImageContainer_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AlbumImageHover.Visibility = Visibility.Visible;
    }

    private void ImageContainer_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        AlbumImageHover.Visibility = Visibility.Collapsed;
    }

    private void FlyoutBtnVolume_OnClick(object sender, RoutedEventArgs e)
    {
        FlyoutBtnVolume.ContextFlyout?.ShowAt(BtnMore);
    }

    private void FlyoutBtnPlayList_OnClick(object sender, RoutedEventArgs e)
    {
        FlyoutBtnPlayList.ContextFlyout?.ShowAt(BtnMore);
        ButtonPlayList_OnClick(sender, e);
    }

    internal void OnEnteringForeground()
    {
        ViewModel.SyncFromState();
        LoadPlayingFile(ViewModel.NowPlayingItem);
        RefreshPlayBarCover(ViewModel.NowPlayingItem);
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        InitializedAni.Begin();
        ViewModel.SetVolumeCommand.Execute((double)_setting.Volume);
        SliderAudioRate.Value = (double)_setting.Volume;
        ViewModel.SyncFromState();
        RefreshPlayModeDisplay();
        _enteredForegroundListener?.Detach();
        _stateChangedListener?.Detach();
        _surfaceStoreChangedListener?.Detach();
        _playlistChangedListener?.Detach();
        _songLikeStatusChangedListener?.Detach();
        _loginCompletedListener?.Detach();
        _enteredForegroundListener = new WeakEventListener<PlayBar, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.OnEnteringForeground(),
            OnDetachAction = weakEventListener => { _lifecycle.EnteredForeground -= weakEventListener.OnEvent; }
        };
        _lifecycle.EnteredForeground += _enteredForegroundListener.OnEvent;
        _stateChangedListener = new WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _surfaceStoreChangedListener = new WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, source, args) => instance.OnSurfaceStorePropertyChanged((PlaybackSurfaceStore)source, args.PropertyName),
            OnDetachAction = weakEventListener => { _surfaceStore.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _surfaceStore.PropertyChanged += _surfaceStoreChangedListener.OnEvent;
        _playlistChangedListener = new WeakEventListener<PlayBar, object?, PlaylistChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.OnPlaylistChanged(),
            OnDetachAction = weakEventListener => { _playlist.PlaylistChanged -= weakEventListener.OnEvent; }
        };
        _playlist.PlaylistChanged += _playlistChangedListener.OnEvent;
        _songLikeStatusChangedListener = new WeakEventListener<PlayBar, object?, SongLikeStatusChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.HyPlayList_OnSongLikeStatusChange(args.IsLiked),
            OnDetachAction = weakEventListener => { _auth.SongLikeStatusChanged -= weakEventListener.OnEvent; }
        };
        _auth.SongLikeStatusChanged += _songLikeStatusChangedListener.OnEvent;
        _loginCompletedListener = new WeakEventListener<PlayBar, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.HyPlayListOnOnLoginDone(),
            OnDetachAction = weakEventListener => { _auth.LoginCompleted -= weakEventListener.OnEvent; }
        };
        _auth.LoginCompleted += _loginCompletedListener.OnEvent;
        if (_surfaceStore.HasPendingExpandedIntent)
            TryExpandPendingSurface();
        else if (_surfaceCoordinator.IsExpanded)
            OnPlaybackSurfaceModeChanged(PlaybackSurfaceMode.Expanded);

        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            ButtonDesktopLyrics.Visibility = Visibility.Collapsed;
        _diagnostics.Logs.Add("Now PlaySource is " + ViewModel.PlaySourceId);

        if (_setting.hotlyricOnStartup)
            try
            {
                var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
                if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri,
                        "306200B4771A6.217957860C1A5_mb3g82vhcggpy") ==
                    LaunchQuerySupportStatus.Available)
                {
                    await Launcher.LaunchUriAsync(uri);
                    Bindings.Update();
                    return;
                }
            }
            catch
            {
            }
        ViewModel.DataTransferManager.DataRequested += DataTransferManager_DataRequested;
    }

    private void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetWebLink(new Uri("https://music.163.com/#/song?id=" +
                                       ViewModel.NowPlayingItem.Id));
        dataPackage.Properties.Title = ViewModel.NowPlayingItem.Name;
        dataPackage.Properties.Description =
            "歌手: " + string.Join(';',
                ViewModel.NowPlayingItem.Artist
                    .Select(t => t.Name));
        var request = args.Request;
        request.Data = dataPackage;
    }

    public async void RefreshPlayBarCover(HyPlayItem? playItem)
    {
        if (ViewModel.CoverStream == null) return;
        _taskRunner.Forget(_notification.InvokeOnUIThread(async () =>
        {
            if (GridSongInfo.Visibility == Visibility.Visible && Opacity != 0)
            {
                try
                {
                    if (playItem != ViewModel.NowPlayingItem) return;
                    using var stream = ViewModel.CoverStream.CloneStream();
                    await AlbumImageSource.SetSourceAsync(stream);
                }
                catch
                {
                    //Ignore
                }
            }
        }), "refresh play bar cover");
    }

    private void HyPlayList_OnSongLikeStatusChange(bool isLiked)
    {
        IconLiked.Visibility = isLiked
            ? Visibility.Visible
            : Visibility.Collapsed;
        FlyoutLiked.Foreground = isLiked
            ? new SolidColorBrush(Colors.Red)
            : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
        FlyoutLiked.Glyph = isLiked
            ? "\uE00B"
            : "\uE006";
    }

    private async void HyPlayListOnOnLoginDone()
    {
        if (ViewModel.PlaySourceId == "local") return;
        try
        {
            var state = await HistoryManagement.GetCurPlayingListHistoryStateAsync();
            if (state.Songs.Count > 0)
            {
                _playlist.AppendNcSongs(state.Songs);
                var restoreIndex = state.CurrentIndex;
                if (restoreIndex < 0 || restoreIndex >= _playlist.Items.Count)
                    restoreIndex = _playlist.Items.Count > 0 ? 0 : -1;

                if (restoreIndex >= 0)
                {
                    var nowItem = _playlist.Items[restoreIndex];
                    await _control.LoadAndPlayAsync(nowItem, setAsPrimary: true, autoPlay: false, removeCurrentSongs: true);
                    _playlist.RestoreNowPlayingIndex(restoreIndex);
                    RunOnUIThread(() =>
                    {
                        var targetingIndex = ViewModel.GetTargetingIndex();
                        if (targetingIndex >= 0 && targetingIndex < PlayItems.Count)
                        {
                            ListBoxPlayList.ScrollIntoView(PlayItems[targetingIndex]);
                        }
                    });
                }

            }
        }
        catch
        {
            // ignored
        }
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        _setting.ABStartPoint = _player.PrimaryAudioInputNode.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        _setting.ABEndPoint = _player.PrimaryAudioInputNode.Position;
    }

    private void ABRepeatStateButton_Click(object sender, RoutedEventArgs e)
    {
        _setting.ABRepeatStatus = !_setting.ABRepeatStatus;
    }

    private void SliderProgress_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _slidingEventArgs = null;
        var value = TimeSpan.FromMilliseconds(SliderProgress.Value);
        if (Math.Abs((value - StartingTimeSpan).TotalMilliseconds) > 250d)
        {
            ViewModel.SeekCommand.Execute(value);
        }

        _isSliding = false;
    }

    private void SliderProgress_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        _isSliding = true;
        _slidingEventArgs = e;
    }

    private void SliderProgress_OnManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
    {
        var value = TimeSpan.FromMilliseconds(SliderProgress.Value);
        StartingTimeSpan = value;
        ViewModel.SeekCommand.Execute(value);
    }

    private void CopySongDetailFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        switch ((sender?.As<MenuFlyoutItem>()).Name)
        {
            case "CopySongNameFlyoutItem":
                if (TbSongName.Text == null) return;
                package.SetText(TbSongName.Text);
                break;
            case "CopySingerNameFlyoutItem":
                if (TbSingerName.Content == null) return;
                package.SetText(TbSingerName.Content.ToString());
                break;
            case "CopyAlbumNameFlyoutItem":
                if (TbAlbumName.Content == null) return;
                package.SetText(TbAlbumName.Content.ToString());
                break;
        }

        package.RequestedOperation = DataPackageOperation.Copy;
        Clipboard.SetContent(package);
    }

    private void BtnReverse_Click(object sender, RoutedEventArgs e)
    {
        _playlist.ReverseList();
        ViewModel.NotifyAppendDone();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _enteredForegroundListener?.Detach();
        _stateChangedListener?.Detach();
        _surfaceStoreChangedListener?.Detach();
        _playlistChangedListener?.Detach();
        _songLikeStatusChangedListener?.Detach();
        _loginCompletedListener?.Detach();
        ViewModel.DataTransferManager.DataRequested -= DataTransferManager_DataRequested;
    }

    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(_notification.InvokeOnUIThread(action), "PlayBar UI update");
    }
}
