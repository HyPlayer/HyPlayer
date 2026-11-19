#region

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Library;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Playlist;
using HyPlayer.Features.Search;
using HyPlayer.Features.User;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Shell;
using HyPlayer.Shell.ExpandedPlayer;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using Microsoft.Graphics.Canvas.Effects;
using WinRT;
using ColorStop = (float offset, Windows.UI.Color color);

#endregion

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly IPlayBarAutoHideService _playBarAutoHide =
        Ioc.Default.GetRequiredService<IPlayBarAutoHideService>();

    private readonly ApiSettings _apiSettings = Ioc.Default.GetRequiredService<ApiSettings>();
    private readonly IShellHostStateService _shellHost = Ioc.Default.GetRequiredService<IShellHostStateService>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly PlaybackStateService _playbackState = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly IMemoryGovernor _memoryGovernor = Ioc.Default.GetRequiredService<IMemoryGovernor>();
    private bool _playBarAutoHideSubscribed;
    private WeakEventListener<MainPage, object?, PropertyChangedEventArgs>? _surfaceStoreChangedListener;
    private bool _isPlaybarOnShow = true;
    private bool _playbarHidden;
    private bool _playbarHiddenWhenIdle;
    private Storyboard? _playbarHideStoryboard;
    private DispatcherTimer? _idleHideTimer;
    private WeakEventListener<MainPage, object?, PropertyChangedEventArgs>? _playbackStateChangedListener;
    private WeakEventListener<MainPage, object?, PropertyChangedEventArgs>? _uiSettingsChangedListener;
    private const int SurfaceFadeMilliseconds = 350;
    private ConnectedAnimationBatch? _collapseAnimations;
    private Storyboard? _expandedPlayerFadeStoryboard;
    private TaskCompletionSource? _expandedPlayerFadeCompletion;
    private bool _expandedPlayerReclaimHeld;
    private int _surfaceTransitionVersion;

    public HyPlayer.Domain.Settings.UISettings UISettings { get; } =
        Ioc.Default.GetRequiredService<HyPlayer.Domain.Settings.UISettings>();

    public MainPage()
    {
        Ioc.Default.GetRequiredService<IProviderNetworkConfigurationProvidable>()
            .ConfigureClientNetwork(_apiSettings.RealIp, _apiSettings.UseHttp);
        if (UISettings.UISound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        MainFrame.Navigate(typeof(BasePage));
        AttachPlayBarAutoHideListener();
        AttachSurfaceStoreListener();
        UIElement playBarMarginRect = PlayBarMarginBackground?.As<UIElement>();
        if (UISettings.PlaybarProgressiveBlur)
            SetPlayBarMarginBlurEffect(playBarMarginRect);
        Loaded += (_, _) => UpdateHomePlaybar();
        if (UISettings.DisplayMaintain) Ioc.Default.GetRequiredService<IDisplayKeepAwakeService>().RequestActive();
    }

    private void AttachSurfaceStoreListener()
    {
        _surfaceStoreChangedListener?.Detach();
        _surfaceStoreChangedListener = new WeakEventListener<MainPage, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnSurfaceStorePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _surfaceStore.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _surfaceStore.PropertyChanged += _surfaceStoreChangedListener.OnEvent;
        _playbackStateChangedListener?.Detach();
        _playbackStateChangedListener = new WeakEventListener<MainPage, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) =>
            {
                if (args.PropertyName == nameof(PlaybackStateService.IsPlaying))
                    _ = instance.RunOnUIThreadAsync(instance.OnPlaybackStateChanged);
            },
            OnDetachAction = weakEventListener => { _playbackState.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _playbackState.PropertyChanged += _playbackStateChangedListener.OnEvent;
        _uiSettingsChangedListener?.Detach();
        _uiSettingsChangedListener = new WeakEventListener<MainPage, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) =>
            {
                if (args.PropertyName == nameof(HyPlayer.Domain.Settings.UISettings.HidePlaybarOnHome))
                    instance.UpdateHomePlaybar();
                if (args.PropertyName is nameof(HyPlayer.Domain.Settings.UISettings.PlaybarBackgroundAcrylic)
                        or nameof(HyPlayer.Domain.Settings.UISettings.AcrylicBackgroundStatus) &&
                    !instance._surfaceStore.IsExpanded)
                    instance.GridPlayBar.Background = instance.GetCompactPlayBarBackground();
            },
            OnDetachAction = weakEventListener => { UISettings.PropertyChanged -= weakEventListener.OnEvent; }
        };
        UISettings.PropertyChanged += _uiSettingsChangedListener.OnEvent;
        ApplyPlaybarVisibility(false);
        ApplySurfaceMode(_surfaceStore.SurfaceMode);
    }

    private void OnPlaybackStateChanged()
    {
        _idleHideTimer?.Stop();
        if (_playbackState.IsPlaying)
        {
            UpdateHomePlaybar();
            return;
        }

        if (_idleHideTimer == null)
        {
            _idleHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _idleHideTimer.Tick += (_, _) =>
            {
                _idleHideTimer.Stop();
                UpdateHomePlaybar();
            };
        }

        _idleHideTimer.Start();
    }

    private void UpdateHomePlaybar()
    {
        var hidden = UISettings.HidePlaybarOnHome && !_surfaceStore.IsExpanded && !_playbackState.IsPlaying;
        if (hidden == _playbarHiddenWhenIdle) return;
        _playbarHiddenWhenIdle = hidden;
        ApplyPlaybarVisibility(true);
    }

    private void AttachPlayBarAutoHideListener()
    {
        if (_playBarAutoHideSubscribed)
            return;

        _playBarAutoHide.VisibilityChanged += PlayBarAutoHide_VisibilityChanged;
        _playBarAutoHideSubscribed = true;
    }

    private void DetachPlayBarAutoHideListener()
    {
        if (!_playBarAutoHideSubscribed)
            return;

        _playBarAutoHide.VisibilityChanged -= PlayBarAutoHide_VisibilityChanged;
        _playBarAutoHideSubscribed = false;
    }

    private void OnSurfaceStorePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackSurfaceStore.SurfaceMode):
                ApplySurfaceMode(_surfaceStore.SurfaceMode);
                UpdateHomePlaybar();
                if (_surfaceStore.IsExpanded)
                {
                    _expandedPlayerReclaimHeld = true;
                    _memoryGovernor.OnExpandedPlayerVisibilityChanged(true);
                }

                break;
            case nameof(PlaybackSurfaceStore.ExpandedFrameOffsetY):
                ExpandedPlayerPositionOffset.Y = _surfaceStore.ExpandedFrameOffsetY;
                break;
            case nameof(PlaybackSurfaceStore.ExpandedFrameResetRequestId):
                ImageResetPositionAni.Begin();
                break;
            case nameof(PlaybackSurfaceStore.ExpandedSurfaceRestoreRequestId):
                EnsureExpandedPlayerFrame();
                break;
            case nameof(PlaybackSurfaceStore.IsPlayBarSuppressed):
                if (!_surfaceStore.IsExpanded && ExpandedPlayer.Content != null) break;
                ApplyPlaybarVisibility(false);
                if (!_surfaceStore.IsPlayBarSuppressed) RestorePlaybar();
                break;
        }
    }

    private void ApplySurfaceMode(PlaybackSurfaceMode mode)
    {
        if (mode == PlaybackSurfaceMode.Expanded)
            PresentExpandedSurface();
        else
            Ioc.Default.GetRequiredService<HyPlayer.Platform.Runtime.Background.IBackgroundTaskRunner>()
                .Forget(RestoreCompactSurfaceAsync(), "restore compact player surface");
    }

    private void PresentExpandedSurface()
    {
        CancelCollapseTransition();
        if (ExpandedPlayer.Content != null)
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                if (_surfaceStore.IsExpanded) ApplyPlaybarVisibility(false);
            });
        ImageResetPositionAni.Stop();
        ExpandedPlayerPositionOffset.X = 0;
        ExpandedPlayerPositionOffset.Y = 0;
        ExpandedPlayer.IsHitTestVisible = true;
        ExpandedPlayer.Visibility = Visibility.Visible;
        try
        {
            EnsureExpandedPlayerFrame();
        }
        catch (Exception ex)
        {
            ExpandedPlayer.Content = null;
            ExpandedPlayer.IsHitTestVisible = false;
            ExpandedPlayer.Visibility = Visibility.Collapsed;
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("无法打开歌词页面", ex.Message);
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>().Collapse());
            return;
        }

        GridPlayBar.BorderThickness = new Thickness(0);
        MainFrame.Visibility = Visibility.Collapsed;
        GridPlayBarMarginBlur.Visibility = Visibility.Collapsed;
        GridPlayBar.Background = null;
    }

    private async Task RestoreCompactSurfaceAsync()
    {
        CancelCollapseTransition();
        var version = _surfaceTransitionVersion;
        var source = ExpandedPlayer.Content as ExpandedPlayer;
        var animations = UISettings.ExpandAnimation && source != null
            ? source.PrepareCollapseAnimations(PlaybackBar)
            : null;
        _collapseAnimations = animations;

        _shellHost.AppTitleBar?.ReleasePointerCaptures();
        ExpandedPlayer.IsHitTestVisible = false;
        MainFrame.Visibility = Visibility.Visible;

        if (_shellHost.AppTitleBar is { } titleBar)
        {
            var dragRegion = titleBar.FindDescendant("PART_DragRegion")?.As<Grid>();
            Window.Current.SetTitleBar(dragRegion);
        }

        if (animations == null)
        {
            ShowCompactPlaybar(false);
            HideExpandedPlayer();
            if (source != null) await WaitForRenderedFrameAsync();
            ReleaseExpandedPlayer(version, source);
            return;
        }

        _playbarHideStoryboard?.Stop();
        _playbarHideStoryboard = null;
        _playbarHidden = true;
        PlayBarHost.IsHitTestVisible = false;
        PlayBarHost.Visibility = Visibility.Visible;
        PlayBarHost.Opacity = 0;
        GridPlayBarMarginBlur.Opacity = 0;

        var revealed = false;
        try
        {
            // Let all SurfaceMode subscribers reveal the PlayBar targets before layout.
            // Keep the source attached and visible while DWM consumes the queued snapshots:
            // clearing Content here can fail-fast in CVisual::GetWorldTransform.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
            if (version != _surfaceTransitionVersion) return;
            PlaybackBar.UpdateLayout();
            var flight = GridPlayBar.Visibility == Visibility.Visible && PlaybackBar.CanShowCollapseAnimations
                ? animations.StartAsync()
                : Task.CompletedTask;
            var fade = FadeOutExpandedPlayerAsync();
            RevealCompactPlaybar(true);
            revealed = true;
            await Task.WhenAll(flight, fade);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playback collapse transition failed: {ex}");
        }
        finally
        {
            animations.Cancel();
        }

        if (version != _surfaceTransitionVersion || _surfaceStore.IsExpanded ||
            !ReferenceEquals(ExpandedPlayer.Content, source)) return;
        _collapseAnimations = null;
        if (!revealed) RevealCompactPlaybar(false);
        HideExpandedPlayer();
        await WaitForRenderedFrameAsync();
        ReleaseExpandedPlayer(version, source);
    }

    private void ShowCompactPlaybar(bool fade)
    {
        GridPlayBarMarginBlur.Visibility = Visibility.Visible;
        GridPlayBar.BorderThickness = new Thickness(1);
        GridPlayBar.Background = GetCompactPlayBarBackground();
        ApplyPlaybarVisibility(fade);
        RestorePlaybar();
    }

    private void RevealCompactPlaybar(bool fade)
    {
        _playbarHidden = !(_surfaceStore.IsPlayBarSuppressed || _playbarHiddenWhenIdle);
        ShowCompactPlaybar(fade);
    }

    private Task FadeOutExpandedPlayerAsync()
    {
        var completion = new TaskCompletionSource();
        var storyboard = new Storyboard();
        AddPlaybarAnimation(storyboard, ExpandedPlayer, "Opacity", 0, SurfaceFadeMilliseconds, EasingMode.EaseOut);
        storyboard.Completed += (_, _) => completion.TrySetResult();
        _expandedPlayerFadeStoryboard = storyboard;
        _expandedPlayerFadeCompletion = completion;
        storyboard.Begin();
        return Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
    }

    private void HideExpandedPlayer()
    {
        ExpandedPlayer.Visibility = Visibility.Collapsed;
        StopExpandedPlayerFade();
        ImageResetPositionAni.Stop();
        ExpandedPlayerPositionOffset.X = 0;
        ExpandedPlayerPositionOffset.Y = 0;
    }

    private void StopExpandedPlayerFade()
    {
        _expandedPlayerFadeStoryboard?.Stop();
        _expandedPlayerFadeStoryboard = null;
        _expandedPlayerFadeCompletion?.TrySetResult();
        _expandedPlayerFadeCompletion = null;
        ExpandedPlayer.Opacity = 1;
    }

    private void ReleaseExpandedPlayer(int version, ExpandedPlayer? source)
    {
        if (version != _surfaceTransitionVersion || _surfaceStore.IsExpanded ||
            !ReferenceEquals(ExpandedPlayer.Content, source)) return;
        ExpandedPlayer.Content = null;
        ReleaseExpandedPlayerReclaimHold();
    }

    private void ReleaseExpandedPlayerReclaimHold()
    {
        if (!_expandedPlayerReclaimHeld) return;
        _expandedPlayerReclaimHeld = false;
        _memoryGovernor.OnExpandedPlayerVisibilityChanged(false);
    }

    private static Task WaitForRenderedFrameAsync()
    {
        var rendered = new TaskCompletionSource();
        EventHandler<RenderedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            Windows.UI.Xaml.Media.CompositionTarget.Rendered -= handler;
            rendered.TrySetResult();
        };
        Windows.UI.Xaml.Media.CompositionTarget.Rendered += handler;
        return Task.WhenAny(rendered.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
    }

    private Brush? GetCompactPlayBarBackground()
    {
        if (UISettings.PlaybarBackgroundAcrylic) return null;
        return Windows.UI.Xaml.Application.Current
            .Resources[UISettings.AcrylicBackgroundStatus
                ? "GridPlayBarBackgroundAcrylic"
                : "SystemControlAcrylicElementMediumHighBrush"].As<Brush>();
    }

    private void CancelCollapseTransition()
    {
        // Invalidate the continuation before Cancel completes its pending task.
        _surfaceTransitionVersion++;
        var animations = _collapseAnimations;
        _collapseAnimations = null;
        animations?.Cancel();
        StopExpandedPlayerFade();
    }

    private void EnsureExpandedPlayerFrame()
    {
        ExpandedPlayer.Visibility = Visibility.Visible;
        ExpandedPlayer.Content ??= new ExpandedPlayer();
        if (ExpandedPlayer.Content is ExpandedPlayer { IsLoaded: true } page)
            page.RestoreTitleBar();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CancelCollapseTransition();
        if (!_surfaceStore.IsExpanded)
        {
            ExpandedPlayer.Content = null;
            ExpandedPlayer.Visibility = Visibility.Collapsed;
            ReleaseExpandedPlayerReclaimHold();
        }
        base.OnNavigatedFrom(e);
        _surfaceStoreChangedListener?.Detach();
        _surfaceStoreChangedListener = null;
        _playbackStateChangedListener?.Detach();
        _playbackStateChangedListener = null;
        _uiSettingsChangedListener?.Detach();
        _uiSettingsChangedListener = null;
        _idleHideTimer?.Stop();
        DetachPlayBarAutoHideListener();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AttachSurfaceStoreListener();
        if (ApplicationView.GetForCurrentView().IsFullScreenMode)
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        var navigation = Ioc.Default.GetRequiredService<INavigationService>();
        switch (e.Parameter)
        {
            case "search":
                navigation.Navigate(typeof(Search));
                break;
            case "account":
                navigation.Navigate(typeof(Me));
                break;
            case "likedsongs":
                var userLibrary = Ioc.Default.GetRequiredService<IUserLibraryStateService>();
                if (userLibrary.LikedSongsPlaylist is { ActualId: { Length: > 0 } likedSongs })
                    navigation.Navigate(typeof(SongListDetail), likedSongs);
                break;
            case "local":
                navigation.Navigate(typeof(LocalMusicPage));
                break;
        }
    }

    private void ApplyPlaybarVisibility(bool slide)
    {
        var hidden = _surfaceStore.IsPlayBarSuppressed || _playbarHiddenWhenIdle;
        if (_playbarHidden == hidden) return;
        _playbarHidden = hidden;
        var storyboard = new Storyboard();
        if (!hidden)
        {
            PlayBarHost.IsHitTestVisible = true;
            PlayBarHost.Visibility = Visibility.Visible;
            if (!_surfaceStore.IsExpanded) GridPlayBarMarginBlur.Visibility = Visibility.Visible;
            _surfaceStore.IsPlayBarSpaceReserved = true;
            if (!slide)
            {
                _playbarHideStoryboard?.Stop();
                _playbarHideStoryboard = null;
                PlayBarHost.Opacity = 1;
                PlayBarHostOffset.Y = 0;
                GridPlayBarMarginBlur.Opacity = 1;
                return;
            }

            AddPlaybarAnimation(storyboard, PlayBarHost, "Opacity", 1, 350, EasingMode.EaseOut);
            AddPlaybarAnimation(storyboard, PlayBarHostOffset, "Y", 0, 350, EasingMode.EaseOut);
            AddPlaybarAnimation(storyboard, GridPlayBarMarginBlur, "Opacity", 1, 350, EasingMode.EaseOut);
            storyboard.Completed += (_, _) =>
            {
                if (_playbarHidden || _playbarHideStoryboard != storyboard) return;
                PlayBarHost.Opacity = 1;
                PlayBarHostOffset.Y = 0;
                GridPlayBarMarginBlur.Opacity = 1;
            };
            _playbarHideStoryboard = storyboard;
            storyboard.Begin();
            return;
        }

        PlayBarHost.IsHitTestVisible = false;
        AddPlaybarAnimation(storyboard, PlayBarHost, "Opacity", 0, slide ? 400 : 250,
            slide ? EasingMode.EaseIn : EasingMode.EaseOut);
        if (slide)
        {
            AddPlaybarAnimation(storyboard, PlayBarHostOffset, "Y", 80, 400, EasingMode.EaseIn);
            AddPlaybarAnimation(storyboard, GridPlayBarMarginBlur, "Opacity", 0, 400, EasingMode.EaseIn);
        }

        storyboard.Completed += (_, _) =>
        {
            if (!_playbarHidden || _playbarHideStoryboard != storyboard) return;
            PlayBarHost.Visibility = Visibility.Collapsed;
            PlayBarHost.Opacity = 0;
            if (!slide) return;
            PlayBarHostOffset.Y = 80;
            GridPlayBarMarginBlur.Opacity = 0;
            if (!_playbarHiddenWhenIdle) return;
            GridPlayBarMarginBlur.Visibility = Visibility.Collapsed;
            _surfaceStore.IsPlayBarSpaceReserved = false;
        };
        _playbarHideStoryboard = storyboard;
        storyboard.Begin();
    }

    private static void AddPlaybarAnimation(Storyboard storyboard, DependencyObject target, string property,
        double to, int milliseconds, EasingMode easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CircleEase { EasingMode = easing }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void RestorePlaybar()
    {
        _playBarAutoHide.SecondCounter = 0;
        _playBarAutoHide.IsVisible = true;
        ShowBar();
    }

    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!isActivated && !_playBarAutoHide.IsActive) return;
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (isActivated)
                ShowBar();
            else
                CollapseBar().SafeFireAndForget();
        });
    }

    private void PlayBarAutoHide_VisibilityChanged(object? sender, PlayBarVisibilityChangedEventArgs e)
    {
        OnPlaybarVisibilityChanged(e.IsActivated);
    }

    private void ShowBar()
    {
        if (!_isPlaybarOnShow)
        {
            PointerInAni.Begin();
            _isPlaybarOnShow = true;
        }
    }

    private Task CollapseBar()
    {
        _isPlaybarOnShow = false;
        var playBarAni = new DoubleAnimation
        {
            To = 0,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut }
        };
        var playBarTransAni = new DoubleAnimation
        {
            To = 20,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut }
        };
        var playBarBlurTransAni = new DoubleAnimation
        {
            To = 0,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut }
        };
        var pointerOutAni = new Storyboard();
        Storyboard.SetTarget(playBarAni, GridPlayBar);
        Storyboard.SetTarget(playBarTransAni, PlayBarTrans);
        Storyboard.SetTarget(playBarBlurTransAni, GridPlayBarMarginBlur);
        Storyboard.SetTargetProperty(playBarAni, "Opacity");
        Storyboard.SetTargetProperty(playBarBlurTransAni, "Opacity");
        Storyboard.SetTargetProperty(playBarTransAni, "Y");
        pointerOutAni.Children.Add(playBarAni);
        pointerOutAni.Children.Add(playBarTransAni);
        pointerOutAni.Children.Add(playBarBlurTransAni);
        pointerOutAni.Begin();
        return Task.CompletedTask;
    }

    private static void SetPlayBarMarginBlurEffect(UIElement sender)
    {
        var helper = new LinearGradientBlurVisualHelper(Window.Current.Compositor);
        ElementCompositionPreview.SetElementChildVisual(sender, helper.RootVisual);
    }

    private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.IsGenerated) return;
        Ioc.Default.GetRequiredService<IPlayBarAutoHideService>().Show();
    }

    internal class LinearGradientBlurVisualHelper
    {
        private const float DefaultMaxBlurAmount = 64f;
        private readonly ColorStop[][] _colorStops;
        private readonly Compositor _compositor;
        private readonly SpriteVisual _rootVisual;
        private readonly SpriteVisual _tintColorVisual;
        private readonly SpriteVisual[] _visuals;

        private Color _tintColor;

        public LinearGradientBlurVisualHelper(Compositor compositor)
        {
            this._compositor = compositor;

            _tintColor = Color.FromArgb(0, 0, 0, 0);

            var dColor = Color.FromArgb(255, 0, 0, 0);
            var hColor = Color.FromArgb(0, 0, 0, 0);

            _visuals = new SpriteVisual[8];
            _colorStops =
            [
                [(0f, dColor), (0.125f, hColor)],
                [(0f, dColor), (0.125f, dColor), (0.25f, hColor)],
                [(0f, hColor), (0.125f, dColor), (0.25f, dColor), (0.375f, hColor)],
                [(0.125f, dColor), (0.25f, hColor), (0.375f, dColor), (0.5f, hColor)],
                [(0.25f, dColor), (0.375f, hColor), (0.5f, dColor), (0.625f, hColor)],
                [(0.375f, dColor), (0.5f, hColor), (0.625f, dColor), (0.75f, hColor)],
                [(0.5f, dColor), (0.625f, hColor), (0.75f, dColor), (0.875f, hColor)],
                [(0.625f, dColor), (0.75f, hColor), (0.875f, dColor), (1, hColor)]
            ];

            _rootVisual = compositor.CreateSpriteVisual();
            _rootVisual.RelativeSizeAdjustment = Vector2.One;

            for (var i = 0; i < _visuals.Length; i++)
            {
                var blurAmount = DefaultMaxBlurAmount;
                for (var j = 0; j < i; j++) blurAmount /= 2;
                _rootVisual.Children.InsertAtTop(_visuals[i] = CreateVisual(compositor, blurAmount, _colorStops[i]));
            }

            _rootVisual.Children.InsertAtTop(_tintColorVisual = CreateTintColorVisual(compositor, _tintColor));
        }

        public Visual RootVisual => _rootVisual;

        public Color TintColor
        {
            get => _tintColor;
            set
            {
                if (_tintColor != value)
                {
                    _tintColor = value;
                    if (_tintColorVisual.Brush is CompositionLinearGradientBrush brush
                        && brush.ColorStops.Count == 2)
                    {
                        brush.ColorStops[0].Color = value;
                        brush.ColorStops[1].Color = MakeTransparent(value);
                    }
                }
            }
        }

        public float MaxBlurAmount
        {
            get => DefaultMaxBlurAmount;
            set
            {
                if (DefaultMaxBlurAmount != value)
                    for (var i = 0; i < _visuals.Length; i++)
                    {
                        var blurAmount = DefaultMaxBlurAmount;
                        for (var j = 0; j < i; j++) blurAmount /= 2;
                        _visuals[i].Brush.Properties.InsertScalar("BlurEffect.BlurAmount", blurAmount);
                    }
            }
        }

        private static Color MakeTransparent(Color color)
        {
            return Color.FromArgb(0, color.R, color.G, color.B);
        }

        private static SpriteVisual CreateTintColorVisual(Compositor compositor, Color tintColor)
        {
            var visual = compositor.CreateSpriteVisual();

            var tintColorBrush = compositor.CreateLinearGradientBrush();
            tintColorBrush.StartPoint = new Vector2(0, 1);
            tintColorBrush.EndPoint = new Vector2(0, 0);
            tintColorBrush.MappingMode = CompositionMappingMode.Relative;

            var color1 = tintColor;
            var color2 = MakeTransparent(color1);

            tintColorBrush.ColorStops.Add(compositor.CreateColorGradientStop(0f, color1));
            tintColorBrush.ColorStops.Add(compositor.CreateColorGradientStop(1f, color2));

            visual.Brush = tintColorBrush;
            visual.RelativeSizeAdjustment = Vector2.One;

            return visual;
        }

        private static SpriteVisual CreateVisual(Compositor compositor, float blurAmount,
            params (float offset, Color color)[] stops)
        {
            var effect = new AlphaMaskEffect
            {
                AlphaMask = new CompositionEffectSourceParameter("mask"),
                Source = new GaussianBlurEffect
                {
                    Name = "BlurEffect",
                    BlurAmount = blurAmount,
                    BorderMode = EffectBorderMode.Soft,
                    Source = new CompositionEffectSourceParameter("source")
                }
            };
            string[] array = ["BlurEffect.BlurAmount"];
            var brush = compositor.CreateEffectFactory(effect, array).CreateBrush();

            var maskBrush = compositor.CreateLinearGradientBrush();

            maskBrush.StartPoint = new Vector2(0, 1);
            maskBrush.EndPoint = new Vector2(0, 0);
            maskBrush.MappingMode = CompositionMappingMode.Relative;

            for (var i = 0; i < stops.Length; i++)
                maskBrush.ColorStops.Add(compositor.CreateColorGradientStop(stops[i].offset, stops[i].color));

            brush.SetSourceParameter("source", compositor.CreateBackdropBrush());
            brush.SetSourceParameter("mask", maskBrush);

            var visual = compositor.CreateSpriteVisual();
            visual.RelativeSizeAdjustment = Vector2.One;
            visual.Brush = brush;
            return visual;
        }
    }
}
