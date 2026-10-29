#region

using System;
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
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Comments;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Services;
using ObservableCollections;
using HyPlayer.Features.User;
using HyPlayer.Platform.Playback.MediaSources;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.Input;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.UI.Dialogs;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using WinRT;
using HyPlayer.Platform.Xaml;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了"用户控件"项模板

namespace HyPlayer.UI.Playback.PlayBar;

public sealed partial class PlayBar
{
    // ---------------------------------------------------------------
    //  ViewModel (resolved from DI; holds all business logic)
    // ---------------------------------------------------------------
    public PlayBarViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<PlayBarViewModel>();

    // ---------------------------------------------------------------
    //  UI-only fields (kept in code-behind)
    // ---------------------------------------------------------------
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly PlaybackSettings _playbackSettings = Ioc.Default.GetRequiredService<PlaybackSettings>();
    private readonly LyricSettings _lyricSettings = Ioc.Default.GetRequiredService<LyricSettings>();
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

    private readonly ILocalFileImportService _localFileImport =
        Ioc.Default.GetRequiredService<ILocalFileImportService>();

    private readonly IPersonalRadioProvidable _personalRadioProvider =
        Ioc.Default.GetRequiredService<IPersonalRadioProvidable>();

    private readonly IPlaybackSurfaceCoordinator _surfaceCoordinator =
        Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();

    private const double AdvancedOperationsMinWindowWidth = 1000;
    private const double CompactWindowWidth = 860;
    private readonly WideLayout _wideLayout;
    private CompactLayout _appliedLayout = CompactLayout.Wide;
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly IAppLifecycleStateService _lifecycle = Ioc.Default.GetRequiredService<IAppLifecycleStateService>();
    private readonly IKeyboardShortcutService _keyboardShortcuts =
        Ioc.Default.GetRequiredService<IKeyboardShortcutService>();
    private WeakEventListener<PlayBar, object?, EventArgs>? _enteredForegroundListener;
    private WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>? _stateChangedListener;
    private WeakEventListener<PlayBar, object?, PropertyChangedEventArgs>? _surfaceStoreChangedListener;
    private WeakEventListener<PlayBar, object?, PlaybackSurfaceCommandRequestedEventArgs>? _surfaceCommandListener;
    private WeakEventListener<PlayBar, object?, SongLikeStatusChangedEventArgs>? _songLikeStatusChangedListener;
    private WeakEventListener<PlayBar, object?, EventArgs>? _loginCompletedListener;
    private WeakEventListener<PlayBar, object?, EventArgs>? _muteChangedListener;
    private DataTransferManager? _dataTransferManager;

    private SolidColorBrush _backgroundElayBrush = new(Colors.Transparent);
    private bool _isSliding;
    private bool _playBarStyleApplied;
    private TimeSpan _startingTimeSpan = TimeSpan.Zero;
    public HyPlayer.Domain.Settings.UISettings UISettings { get; } =
        Ioc.Default.GetRequiredService<HyPlayer.Domain.Settings.UISettings>();
    public NotifyCollectionChangedSynchronizedViewList<PlayBarQueueItem> PlayItems { get; }

#nullable enable
    private ManipulationStartedRoutedEventArgs? _slidingEventArgs;
#nullable restore

    /*
private Storyboard TbSongNameScrollStoryBoard;
private double lastOffsetX;
DoubleAnimation verticalAnimation;
*/

    public PlayBar()
    {
        PlayItems = ViewModel.PlaylistItems.ToNotifyCollectionChanged();
        InitializeComponent();
        _wideLayout = CaptureWideLayout();
        SizeChanged += (_, _) =>
        {
            ApplyResponsiveControls();
            UpdateAdvancedOperationsVisibility();
            UpdateProgressClearance();
        };
        SongInfoTag.SizeChanged += (_, _) => UpdateProgressClearance();
        SongInfoText.SizeChanged += (_, _) => UpdateProgressClearance();
        PlayProgress.SizeChanged += (_, _) => UpdateProgressClearance();
    }

    private void UpdateProgressClearance()
    {
        if (!IsLoaded || PlayProgress.ActualWidth <= 0) return;
        try
        {
            var clearance = 0d;
            if (SongInfoTag.ActualWidth > 0 && SongInfoTag.Visibility == Visibility.Visible &&
                GridSongInfo.Visibility == Visibility.Visible &&
                GridSongInfoContainer.Visibility == Visibility.Visible)
            {
                var tag = SongInfoTag.TransformToVisual(this)
                    .TransformBounds(new Windows.Foundation.Rect(0, 0, SongInfoTag.ActualWidth, SongInfoTag.ActualHeight));
                var progress = PlayProgress.TransformToVisual(this)
                    .TransformBounds(new Windows.Foundation.Rect(0, 0, PlayProgress.ActualWidth, PlayProgress.ActualHeight));
                var overlapsVertically = tag.Bottom > progress.Top && tag.Top < progress.Bottom;
                clearance = overlapsVertically ? Math.Max(0, tag.Right + 14 - progress.Left) : 0;
            }

            if (Math.Abs(PlayProgress.Padding.Left - clearance) > 0.5)
                PlayProgress.Padding = new Thickness(clearance, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Play bar progress clearance failed: {ex.Message}");
        }
    }

    private WideLayout CaptureWideLayout()
    {
        return new WideLayout(MainControlGrid.Margin, Grid.GetColumn(MainControlGrid), Grid.GetColumnSpan(MainControlGrid),
            MainBtnsGrid.Margin, PlayProgress.Margin);
    }

    private void ApplyResponsiveControls()
    {
        var width = Window.Current?.Bounds.Width ?? ActualWidth;
        var layout = width >= CompactWindowWidth
            ? CompactLayout.Wide
            : _surfaceStore.IsExpanded ? CompactLayout.Expanded : CompactLayout.Docked;
        if (layout == _appliedLayout) return;
        _appliedLayout = layout;
        var extras = layout == CompactLayout.Wide ? Visibility.Visible : Visibility.Collapsed;
        Btns.Visibility = extras;
        BtnMore.Visibility = extras;
        BtnPlayRollType.Visibility = extras;
        if (layout == CompactLayout.Wide)
        {
            Grid.SetColumn(MainControlGrid, _wideLayout.MainColumn);
            Grid.SetColumnSpan(MainControlGrid, _wideLayout.MainColumnSpan);
            MainControlGrid.Margin = _wideLayout.MainMargin;
            MainBtnsGrid.Margin = _wideLayout.ButtonsMargin;
            PlayProgress.Margin = _wideLayout.ProgressMargin;
        }
        else
        {
            var expanded = layout == CompactLayout.Expanded;
            Grid.SetColumn(MainControlGrid, expanded ? 0 : 1);
            Grid.SetColumnSpan(MainControlGrid, expanded ? 4 : 3);
            MainControlGrid.Margin = new Thickness(0);
            MainBtnsGrid.Margin = new Thickness(0, 5, 0, 0);
            PlayProgress.Margin = new Thickness(16, -5, 16, 0);
        }

        UpdateProgressClearance();
    }

    private void UpdateAdvancedOperationsVisibility()
    {
        var show = _surfaceStore.PlayBarProjection.ShowAdvancedOperations &&
                   Window.Current?.Bounds.Width >= AdvancedOperationsMinWindowWidth;
        GridSongAdvancedOperation.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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
            case nameof(PlaybackStateService.Volume):
                RunOnUIThread(SyncVolumeSlider);
                break;
            case nameof(PlaybackStateService.IsTransitioning):
                RunOnUIThread(RefreshTransitionEffect);
                break;
            case nameof(PlaybackStateService.IsPlaying):
                RunOnUIThread(RefreshBackgroundBreath);
                break;
            case nameof(PlaybackStateService.IsInFm):
            case nameof(PlaybackStateService.ActiveStrategyId):
                RunOnUIThread(() =>
                {
                    ViewModel.SyncFromState();
                    RefreshPlayModeDisplay();
                });
                break;
        }
    }

    private Brush? GetBarBackground(bool acrylic)
    {
        return acrylic
            ? Windows.UI.Xaml.Application.Current.Resources["SystemControlAcrylicWindowBrush"]?.As<Brush>()
            : null;
    }

    private void RefreshBackgroundBreath()
    {
        if (_state.IsPlaying && UISettings.PlaybarBackgroundBreath)
            PlayBarBackgroundAni.Begin();
        else
            PlayBarBackgroundAni.Stop();
    }

    private void ApplyPlayBarStyle()
    {
        if (_playBarStyleApplied) return;
        _playBarStyleApplied = true;
        if (!UISettings.PlaybarButtonsTransparent)
        {
            BtnPlayRollType.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
            BtnPreviousSong.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
            BtnPlayStateChange.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
            BtnNextSong.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
            BtnLike.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
        }

        if (UISettings.PlayButtonAccentColor)
        {
            BtnPlayStateChange.Background = Resources["SolidPlayButtonColor"]?.As<Brush>();
            PlayStateIcon.Foreground = Resources["SolidPlayButtonIconColor"]?.As<Brush>();
        }
        else
        {
            PlayBarBackgroundAni.Children.RemoveAt(2);
        }

        if (!UISettings.PlaybarBackgroundElay) return;
        PointerEntered += (_, _) =>
        {
            if (_surfaceStore.IsExpanded && UISettings.PlaybarBackgroundElay)
                GridThis.Background = _backgroundElayBrush;
        };
        PointerExited += (_, _) => { GridThis.Background = new SolidColorBrush(Colors.Transparent); };
    }

    private void OnSurfaceCommandRequested(PlaybackSurfaceCommandRequestedEventArgs args)
    {
        switch (args.Command)
        {
            case PlaybackSurfaceCommand.TogglePlayback:
                BtnPlayStateChange_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.MovePrevious:
                BtnPreviousSong_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.MoveNext:
                BtnNextSong_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.ChangePlayMode:
                BtnPlayRollType_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.ToggleLike:
                BtnLike_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.ShowQueue:
                if (args.Anchor is FrameworkElement anchor)
                {
                    ButtonPlayList.Flyout?.ShowAt(anchor);
                    ButtonPlayList_OnClick(this, null);
                }

                break;
            case PlaybackSurfaceCommand.OpenDesktopLyrics:
                ToggleButton_Click(this, null);
                break;
            case PlaybackSurfaceCommand.AddToSongList:
                Btn_Sub_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.Download:
                Btn_Down_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.OpenComments:
                Btn_Comment_OnClick(this, null);
                break;
            case PlaybackSurfaceCommand.Share:
                Btn_Share_OnClick(this, null);
                break;
        }
    }

    private HyPlayer.UI.Controls.Primitives.PlayBarTransitionEffect _transitionEffect;

    private void RefreshTransitionEffect()
    {
        if (!IsLoaded) return;
        _transitionEffect ??= new HyPlayer.UI.Controls.Primitives.PlayBarTransitionEffect(
            SliderProgress, SongInfoTag, TbSongTag, TbTransitionTag, TransitionTagGlowHost);
        _transitionEffect.SetActive(_state.IsTransitioning);
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
                {
                    ApplyPlaybackTheme(store.Theme);
                    if (UISettings.PlaybarBackgroundElay) SetPlayBarIdleBackground(store.Theme.IdleBrush);
                }
                else
                {
                    ApplyCompactPlaybackTheme();
                }

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
            ApplyResponsiveControls();
            UpdateAdvancedOperationsVisibility();
            UpdateProgressClearance();
            if (isExpanded)
                PlayBarBackgroundFadeOut.Begin();
            else
                PlayBarBackgroundFadeIn.Begin();

            if (!isExpanded)
                ApplyCompactPlaybackTheme();

            if (!isExpanded)
                RefreshPlayBarCover(ViewModel.NowPlayingProviderItem);
        });
    }

    private void ApplyPlaybackTheme(PlaybackThemeSnapshot theme)
    {
        ViewModel.DisplayedTheme = theme;
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

    internal bool CanShowCollapseAnimations => GridSongInfoContainer.Visibility == Visibility.Visible &&
                                               GridSongInfo.Visibility == Visibility.Visible &&
                                               IsLoaded && ActualHeight > 0;

    internal FrameworkElement GetCollapseAnimationTarget(string key) => key switch
    {
        "SongTitle" => TbSongName,
        "SongImg" => AlbumImage,
        "SongArtist" => TbSingerName,
        "SongAlbum" => TbAlbumName,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private void HyPlayListOnOnSongRemoveAll()
    {
        RunOnUIThread(() =>
        {
            ViewModel.PlaylistItems.Clear();
            PlayListTitle.Text = "播放列表";
        });
    }

    public void SetPlayBarIdleBackground(SolidColorBrush colorBrush)
    {
        var color = colorBrush.Color;
        color.A = 80;
        _backgroundElayBrush = new SolidColorBrush(color);
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
        var songId = NeteaseTrack.IdOf(providerItem);
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
            {
                _history.AddNCSongHistory(songId);
                _taskRunner.Forget(_history.RememberSongAsync(songId, providerItem), "remember played song");
            }
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
        var inFm = _state.IsInFm;
        var previousText = inFm ? "退出私人 FM" : "上一首";
        AutomationProperties.SetName(BtnPreviousSong, previousText);
        ToolTipService.SetToolTip(BtnPreviousSong, previousText);
        AutomationProperties.SetName(BtnPlayRollType, inFm ? "我不喜欢" : "播放顺序");
        if (inFm)
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
            await _control.LoadAndPlayAsync(providerItem);
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
        RefreshMuteGlyphs();
    }

    private void SyncVolumeSlider()
    {
        var value = Math.Round(_state.Volume * 100);
        if (Math.Abs(SliderAudioRate.Value - value) >= 1)
            SliderAudioRate.Value = value;
    }

    private void RefreshMuteGlyphs()
    {
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
            ViewModel.MoveToItemCommand.Execute(item);
    }

    private void RequestExpandedPlayer()
    {
        if (!_player.PlayerCreated || _player.PrimaryPlaybackSource == null) return;

        // Prepare ConnectedAnimations from PlayBar elements before coordinator animates ExpandedPlayer
        if (UISettings.ExpandAnimation && GridSongInfoContainer.Visibility == Visibility.Visible)
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
        await _playCore.MovePointerToAsync(items[items.Count - 1]);
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
                _taskRunner.Forget(_personalRadioProvider.MovePersonalRadioItemToTrashAsync(songId),
                    "trash personal radio item");
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
            {
                await new ArtistSelectDialog(creators).ShowAsync();
            }
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
        var songId = NeteaseTrack.IdOf(ViewModel.NowPlayingProviderItem);
        if (!string.IsNullOrEmpty(songId))
            await new SongListSelect(songId).ShowAsync();
    }

    private void Btn_Down_OnClick(object sender, RoutedEventArgs e)
    {
        var providerItem = ViewModel.NowPlayingProviderItem;
        if (providerItem != null) DownloadManager.AddDownload(providerItem);
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
        var toastLyrics = Ioc.Default.GetRequiredService<ToastLyricService>();
        if (toastLyrics.IsEnabled || _lyricSettings.UseToastLyrics)
        {
            toastLyrics.Toggle();
            return;
        }

        // 当前未打开歌词
        var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
        if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri,
                "306200B4771A6.217957860C1A5_mb3g82vhcggpy") != LaunchQuerySupportStatus.Available)
        {
            var dlg = new ContentDialog
            {
                Title = "关于桌面歌词",
                Content =
                    "可以安装「热词」获得桌面歌词，也可以使用 Windows 通知显示歌词。",
                SecondaryButtonText = "使用通知歌词",
                CloseButtonText = "否",
                PrimaryButtonText = "安装 「热词」"
            };

            var res = await dlg.ShowAsync(ContentDialogPlacement.Popup);
            if (res == ContentDialogResult.Primary)
                await Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9"));
            else if (res == ContentDialogResult.Secondary)
            {
                _lyricSettings.UseToastLyrics = true;
                toastLyrics.Toggle();
            }

            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri, new LauncherOptions
            {
                FallbackUri = new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9")
            });
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
            ListBoxPlayList.ScrollIntoView(PlayItems[targetingIndex]);
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
        PlayBarBackgroundFadeIn.Begin();
        ApplyPlayBarStyle();
        RefreshBackgroundBreath();
        if (!_surfaceStore.IsExpanded)
            ApplyCompactPlaybackTheme();

        ViewModel.SetVolumeCommand.Execute(_playbackSettings.Volume);
        SliderAudioRate.Value = _playbackSettings.Volume;
        ViewModel.SyncFromState();
        RefreshPlayModeDisplay();
        RefreshTransitionEffect();
        _enteredForegroundListener?.Detach();
        _stateChangedListener?.Detach();
        _surfaceStoreChangedListener?.Detach();
        _surfaceCommandListener?.Detach();
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
            OnEventAction = static (instance, source, args) =>
                instance.OnSurfaceStorePropertyChanged((PlaybackSurfaceStore)source, args.PropertyName),
            OnDetachAction = weakEventListener => { _surfaceStore.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _surfaceStore.PropertyChanged += _surfaceStoreChangedListener.OnEvent;
        _surfaceCommandListener =
            new WeakEventListener<PlayBar, object?, PlaybackSurfaceCommandRequestedEventArgs>(this)
            {
                OnEventAction = static (instance, _, args) => instance.OnSurfaceCommandRequested(args),
                OnDetachAction = weakEventListener => { _surfaceStore.CommandRequested -= weakEventListener.OnEvent; }
            };
        _surfaceStore.CommandRequested += _surfaceCommandListener.OnEvent;
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
        _muteChangedListener?.Detach();
        _muteChangedListener = new WeakEventListener<PlayBar, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.RunOnUIThread(instance.RefreshMuteGlyphs),
            OnDetachAction = weakEventListener => { _keyboardShortcuts.MuteChanged -= weakEventListener.OnEvent; }
        };
        _keyboardShortcuts.MuteChanged += _muteChangedListener.OnEvent;
        if (_surfaceStore.HasPendingExpandedIntent)
            TryExpandPendingSurface();
        else if (_surfaceCoordinator.IsExpanded)
            OnPlaybackSurfaceModeChanged(PlaybackSurfaceMode.Expanded);

        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            ButtonDesktopLyrics.Visibility = Visibility.Collapsed;
        _diagnostics.AddLog("Now PlaySource is " + ViewModel.PlaySourceId);

        if (_lyricSettings.HotLyricOnStartup)
            try
            {
                var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
                if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri,
                        "306200B4771A6.217957860C1A5_mb3g82vhcggpy") ==
                    LaunchQuerySupportStatus.Available)
                {
                    await Launcher.LaunchUriAsync(uri);
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
            _diagnostics.AddLog("Failed to initialize PlayBar share integration: " + ex.Message);
        }
    }

    private void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dataPackage = new DataPackage();
        var providerItem = ViewModel.NowPlayingProviderItem;
        var songId = NeteaseTrack.IdOf(providerItem);
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

    public void RefreshPlayBarCover(SingleSongBase? providerItem)
    {
        if (ViewModel.CoverStream == null) return;
        _taskRunner.Forget(this.RunOnUIThreadAsync(async () =>
        {
            if (GridSongInfo.Visibility == Visibility.Visible && Opacity != 0)
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

    private void SliderProgress_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _slidingEventArgs = null;
        var value = TimeSpan.FromMilliseconds(SliderProgress.Value);
        if (Math.Abs((value - _startingTimeSpan).TotalMilliseconds) > 250d) ViewModel.SeekCommand.Execute(value);

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
        _startingTimeSpan = value;
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
        _surfaceCommandListener?.Detach();
        _songLikeStatusChangedListener?.Detach();
        _loginCompletedListener?.Detach();
        _muteChangedListener?.Detach();
        if (_dataTransferManager is not null)
            _dataTransferManager.DataRequested -= DataTransferManager_DataRequested;
    }

    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(this.RunOnUIThreadAsync(action), "PlayBar UI update");
    }

    private readonly record struct WideLayout(
        Thickness MainMargin,
        int MainColumn,
        int MainColumnSpan,
        Thickness ButtonsMargin,
        Thickness ProgressMargin);

    private enum CompactLayout
    {
        Wide,
        Docked,
        Expanded
    }
}
