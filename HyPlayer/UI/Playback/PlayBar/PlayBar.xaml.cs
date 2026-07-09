#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.User;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.UI.Dialogs;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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
    private SolidColorBrush _playbackAccentBrush = CreateCompactPlaybackTheme(ElementTheme.Dark).AccentBrush;
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
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IDiagnosticsStateService _diagnostics = Ioc.Default.GetRequiredService<IDiagnosticsStateService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();
    private readonly IPlaybackMemoryService _playbackMemory = Ioc.Default.GetRequiredService<IPlaybackMemoryService>();
    private readonly ILocalFileImportService _localFileImport = Ioc.Default.GetRequiredService<ILocalFileImportService>();
    private readonly IPersonalRadioProvidable _personalRadioProvider = Ioc.Default.GetRequiredService<IPersonalRadioProvidable>();
    private readonly IPlaybackSurfaceCoordinator _surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly IAppLifecycleStateService _lifecycle = Ioc.Default.GetRequiredService<IAppLifecycleStateService>();
    private WeakEventListener<PlayBar, object?, EventArgs>? _enteredForegroundListener;
    private WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>? _stateChangedListener;
    private WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>? _surfaceStoreChangedListener;
    private WeakEventListener<PlayBar, object?, SongLikeStatusChangedEventArgs>? _songLikeStatusChangedListener;
    private WeakEventListener<PlayBar, object?, EventArgs>? _loginCompletedListener;
    private DataTransferManager? _dataTransferManager;

    private SolidColorBrush BackgroundElayBrush = new(Colors.Transparent);
    private bool _isSliding = false;
    private TimeSpan StartingTimeSpan = TimeSpan.Zero;
    public ObservableCollection<PlayBarQueueItem> PlayItems => ViewModel.PlaylistItems;

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

    private void PlayBar_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (!_surfaceStore.IsExpanded)
            ApplyCompactPlaybackTheme();
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.NowPlayingProviderItem):
            case nameof(PlaybackStateService.NowPlayingSnapshot):
                RunOnUIThread(LoadPlayingFile);
                break;
            case nameof(PlaybackStateService.CoverStream):
                RefreshPlayBarCover(_state.NowPlayingProviderItem);
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
                if (store.IsExpanded)
                    ApplyPlaybackTheme(store.Theme);
                else
                    ApplyCompactPlaybackTheme();
                break;
        }
    }

    private void OnPlaylistChanged()
    {
        RunOnUIThread(() =>
        {
            if (ViewModel.QueueCount == 0)
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
                ApplyCompactPlaybackTheme();

            if (!isExpanded)
                StartPreparedCollapseAnimations();

            if (!isExpanded)
                RefreshPlayBarCover(ViewModel.NowPlayingProviderItem);
        });
    }

    private void ApplyPlaybackTheme(PlaybackThemeSnapshot theme)
    {
        PlaybackAccentBrush = theme.AccentBrush;
        PlaybackAccentTheme = theme.IsBright ? ElementTheme.Light : ElementTheme.Dark;
    }

    private void ApplyCompactPlaybackTheme()
    {
        var theme = ActualTheme == ElementTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        ApplyPlaybackTheme(CreateCompactPlaybackTheme(theme));
    }

    private static PlaybackThemeSnapshot CreateCompactPlaybackTheme(ElementTheme theme)
    {
        var isLight = theme == ElementTheme.Light;
        var accentColor = isLight ? Colors.Black : Colors.White;
        var idleColor = isLight
            ? Color.FromArgb(114, 0, 0, 0)
            : Color.FromArgb(66, 255, 255, 255);
        return new PlaybackThemeSnapshot(
            new SolidColorBrush(accentColor),
            new SolidColorBrush(idleColor),
            accentColor,
            isLight);
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

    public void LoadPlayingFile()
    {
        var providerItem = ViewModel.NowPlayingProviderItem;
        var snapshot = ViewModel.NowPlayingSnapshot;
        if (providerItem == null) return;

        RunOnUIThread(() => ApplicationView.GetForCurrentView().Title =
            $"{ViewModel.SongName} - {ViewModel.ArtistName}");

        //SliderAudioRate.Value = ViewModel.Volume * 100;

        RunOnUIThread(() =>
        {
            RefreshPlayModeDisplay();

            // 恢复播放音量
            if (ViewModel.NowPlayingProviderItem == null)
            {
                ApplicationView.GetForCurrentView().Title = "";
                return;
            }

            if (_isSliding)
            {
                _slidingEventArgs?.Complete();
                _isSliding = false;
            }

            SliderProgress.Minimum = 0;
            // Maximum/value/current time are provided by PlayBarViewModel x:Bind.

        });
        var songId = providerItem.ActualId;
        var isLiked = !string.IsNullOrEmpty(songId) && _auth.LikedSongs.Contains(songId);
        if (snapshot?.IsLocal != true)
        {
            RunOnUIThread(() =>
            {
                IconLiked.Visibility = isLiked
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                FlyoutLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Windows.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
                FlyoutLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
            });
            if (!string.IsNullOrEmpty(songId))
                _history.AddNCSongHistory(songId);
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
        var providerItem = ViewModel.NowPlayingProviderItem;
        if (!_player.PlayerCreated || providerItem == null) return;

        if (_player.PrimaryPlaybackSource == null)
        {
            await _control.LoadAndPlayAsync(providerItem, autoPlay: true, removeCurrentSongs: true);
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
        if (ListBoxPlayList.SelectedItem is PlayBarQueueItem item && !item.IsCurrent)
        {
            ViewModel.MoveToItemCommand.Execute(item);
        }
    }

    private void RequestExpandedPlayer()
    {
        if (!_player.PlayerCreated || _player.PrimaryPlaybackSource == null) return;

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
        _taskRunner.Forget(PickAndAppendLocalFilesAsync, "pick local file from play bar");
    }

    private async Task PickAndAppendLocalFilesAsync()
    {
        var items = await _localFileImport.PickLocalFilesAsync();
        if (items.Count == 0)
            return;

        await _playCore.InsertSongRangeAsync(items.Cast<SingleSongBase>().ToList());
        var queueCount = (await _playCore.GetPlaylistAsync()).Count;
        await _playCore.MovePointerToIndexAsync(queueCount - 1);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
    }

    private void PlayListRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var item = btn.DataContext as PlayBarQueueItem;
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
            var songId = ViewModel.NowPlayingProviderItem?.ActualId;
            if (!string.IsNullOrEmpty(songId))
                _taskRunner.Forget(_personalRadioProvider.MovePersonalRadioItemToTrashAsync(songId), "trash personal radio item");
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
            var providerItem = ViewModel.NowPlayingProviderItem;
            if (providerItem is null)
                return;

            var creators = await providerItem.GetCreatorsAsync();
            if (creators is { Count: > 1 })
                await new ArtistSelectDialog(creators).ShowAsync();
            else if (creators is { Count: 1 })
            {
                var creator = creators[0];
                _navigation.Navigate(creator is ArtistBase ? typeof(ArtistPage) : typeof(Me), creator.ActualId);
            }
        }
        catch
        {
        }
    }

    private async void TbAlbumName_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            var providerItem = ViewModel.NowPlayingProviderItem;
            if (providerItem?.Album is { ActualId: { Length: > 0 } albumId } && albumId != "0")
            {
                _navigation.Navigate(typeof(AlbumPage), albumId);
                return;
            }

            var creators = providerItem is null ? null : await providerItem.GetCreatorsAsync();
            if (creators is { Count: 1 })
                _navigation.Navigate(typeof(Me), creators[0].ActualId);
        }
        catch
        {
        }
    }

    private async void Btn_Sub_OnClick(object sender, RoutedEventArgs e)
    {
        var songId = ViewModel.NowPlayingProviderItem?.ActualId;
        if (!string.IsNullOrEmpty(songId))
            await new SongListSelect(songId).ShowAsync();
    }

    private void Btn_Down_OnClick(object sender, RoutedEventArgs e)
    {
        var providerItem = ViewModel.NowPlayingProviderItem;
        if (providerItem != null)
        {
            DownloadManager.AddDownload(providerItem);
        }
    }

    private void Btn_Comment_OnClick(object sender, RoutedEventArgs e)
    {
        var providerItem = ViewModel.NowPlayingProviderItem;
        if (!string.IsNullOrEmpty(providerItem?.ActualId))
            _navigation.Navigate(typeof(Comments), new CommentTarget(providerItem.TypeId, providerItem.ActualId));
        RequestCompactPlayer();
    }

    private void Btn_Share_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingProviderItem is null) return;
        if (_dataTransferManager is null)
        {
            _notification.ShowMessage("分享不可用", "当前窗口未初始化分享服务");
            return;
        }

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
        var targetingIndex = ViewModel.GetTargetingIndex();
        if (targetingIndex >= 0 && targetingIndex < PlayItems.Count)
        {
            ListBoxPlayList.ScrollIntoView(PlayItems[targetingIndex]);
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
        LoadPlayingFile();
        RefreshPlayBarCover(ViewModel.NowPlayingProviderItem);
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= PlayBar_ActualThemeChanged;
        ActualThemeChanged += PlayBar_ActualThemeChanged;
        InitializedAni.Begin();
        if (!_surfaceStore.IsExpanded)
            ApplyCompactPlaybackTheme();

        ViewModel.SetVolumeCommand.Execute((double)_setting.Volume);
        SliderAudioRate.Value = (double)_setting.Volume;
        ViewModel.SyncFromState();
        RefreshPlayModeDisplay();
        _enteredForegroundListener?.Detach();
        _stateChangedListener?.Detach();
        _surfaceStoreChangedListener?.Detach();
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
        try
        {
            _dataTransferManager = DataTransferManager.GetForCurrentView();
            _dataTransferManager.DataRequested += DataTransferManager_DataRequested;
        }
        catch (Exception ex)
        {
            _diagnostics.Logs.Add("Failed to initialize PlayBar share integration: " + ex.Message);
        }
    }

    private void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dataPackage = new DataPackage();
        var providerItem = ViewModel.NowPlayingProviderItem;
        var songId = providerItem?.ActualId;
        if (string.IsNullOrEmpty(songId)) return;
        dataPackage.SetWebLink(new Uri("https://music.163.com/#/song?id=" + songId));
        dataPackage.Properties.Title = providerItem?.Name ?? string.Empty;
        dataPackage.Properties.Description =
            "歌手: " + (providerItem?.CreatorList is { Count: > 0 } creators
                ? string.Join(';', creators)
                : ViewModel.NowPlayingSnapshot?.ArtistText ?? string.Empty);
        var request = args.Request;
        request.Data = dataPackage;
    }

    public async void RefreshPlayBarCover(SingleSongBase? providerItem)
    {
        if (ViewModel.CoverStream == null) return;
        _taskRunner.Forget(_notification.InvokeOnUIThread(async () =>
        {
            if (GridSongInfo.Visibility == Visibility.Visible && Opacity != 0)
            {
                try
                {
                    if (providerItem != ViewModel.NowPlayingProviderItem) return;
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
            : Windows.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
        FlyoutLiked.Glyph = isLiked
            ? "\uE00B"
            : "\uE006";
    }

    private async void HyPlayListOnOnLoginDone()
    {
        if (ViewModel.PlaySourceId == "local") return;
        try
        {
            if ((await _playCore.GetPlaylistAsync()).Count == 0)
                await _playbackMemory.RestoreAsync();
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
        _taskRunner.Forget(_playCore.ReversePlaylistAsync(), "reverse PlayCore queue");
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= PlayBar_ActualThemeChanged;
        _enteredForegroundListener?.Detach();
        _stateChangedListener?.Detach();
        _surfaceStoreChangedListener?.Detach();
        _songLikeStatusChangedListener?.Detach();
        _loginCompletedListener?.Detach();
        if (_dataTransferManager is not null)
            _dataTransferManager.DataRequested -= DataTransferManager_DataRequested;
    }

    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(_notification.InvokeOnUIThread(action), "PlayBar UI update");
    }
}