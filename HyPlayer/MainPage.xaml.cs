#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Library;
using HyPlayer.Features.Playlist;
using HyPlayer.Features.Search;
using HyPlayer.Features.User;
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
using HyPlayer.Platform.Xaml;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Shell;
using HyPlayer.Shell.ExpandedPlayer;
using Microsoft.Graphics.Canvas.Effects;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Numerics;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
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
    bool IsPlaybarOnShow = true;
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly IShellHostStateService _shellHost = Ioc.Default.GetRequiredService<IShellHostStateService>();
    private readonly IPlayBarAutoHideService _playBarAutoHide = Ioc.Default.GetRequiredService<IPlayBarAutoHideService>();
    private WeakEventListener<MainPage, object?, PropertyChangedEventArgs>? _surfaceStoreChangedListener;
    private bool _playBarAutoHideSubscribed;

    public MainPage()
    {
        Ioc.Default.GetRequiredService<HyPlayer.PlayCore.Abstraction.Interfaces.Provider.IProviderNetworkConfigurationProvidable>()
            .ConfigureClientNetwork(Setting.GetSettings<string>("xRealIp", null), Setting.GetSettings("UseHttp", false));
        if (_setting.uiSound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        MainFrame.Navigate(typeof(BasePage));
        AttachPlayBarAutoHideListener();
        AttachSurfaceStoreListener();
        UIElement PlayBarMarginRect = PlayBarMarginBackground?.As<UIElement>();
        SetPlayBarMarginBlurEffect(PlayBarMarginRect);
        ActualThemeChanged += MainPage_ActualThemeChanged;
        if (_setting.displayMaintain)
        {
            Ioc.Default.GetRequiredService<IDisplayKeepAwakeService>().RequestActive();
        }
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
        ApplySurfaceMode(_surfaceStore.SurfaceMode);
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
        }
    }

    private void ApplySurfaceMode(PlaybackSurfaceMode mode)
    {
        if (mode == PlaybackSurfaceMode.Expanded)
            PresentExpandedSurface();
        else
            RestoreCompactSurface();
    }

    private void PresentExpandedSurface()
    {
        ExpandedPlayer.Visibility = Visibility.Visible;
        EnsureExpandedPlayerFrame();
        GridPlayBar.BorderThickness = new Thickness(0);
        MainFrame.Visibility = Visibility.Collapsed;
        GridPlayBarMarginBlur.Visibility = Visibility.Collapsed;
        GridPlayBar.Background = null;
    }

    private void RestoreCompactSurface()
    {
        GridPlayBarMarginBlur.Visibility = Visibility.Visible;
        _shellHost.AppTitleBar?.ReleasePointerCaptures();
        ExpandedPlayer.Content = null;
        ExpandedPlayer.Visibility = Visibility.Collapsed;
        GridPlayBar.BorderThickness = new Thickness(1);
        GridPlayBar.Background = Windows.UI.Xaml.Application.Current.Resources["SystemControlAcrylicElementMediumHighBrush"].As<Brush>();
        MainFrame.Visibility = Visibility.Visible;

        if (_shellHost.AppTitleBar is { } titleBar)
        {
            var dragRegion = titleBar.FindDescendant("PART_DragRegion")?.As<Grid>();
            Window.Current.SetTitleBar(dragRegion);
        }
    }

    private void EnsureExpandedPlayerFrame()
    {
        ExpandedPlayer.Visibility = Visibility.Visible;
        ExpandedPlayer.Content ??= new ExpandedPlayer();
    }

    private void MainPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        _setting.OnPropertyChanged("acrylicBackgroundStatus");
        _setting.OnPropertyChanged("playbarBackgroundAcrylic");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _surfaceStoreChangedListener?.Detach();
        _surfaceStoreChangedListener = null;
        DetachPlayBarAutoHideListener();
        ActualThemeChanged -= MainPage_ActualThemeChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AttachSurfaceStoreListener();
        if (ApplicationView.GetForCurrentView().IsFullScreenMode)
        {
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        }
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
    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!_setting.AutoHidePlaybar) return;
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (isActivated)
            {
                ShowBar();
            }
            else
            {
                CollapseBar().SafeFireAndForget();
            }
        });
    }

    private void PlayBarAutoHide_VisibilityChanged(object? sender, PlayBarVisibilityChangedEventArgs e)
    {
        OnPlaybarVisibilityChanged(e.IsActivated);
    }

    private void ShowBar()
    {
        if (!IsPlaybarOnShow)
        {
            PointerInAni.Begin();
            IsPlaybarOnShow = true;
        }
    }

    private Task CollapseBar()
    {
        IsPlaybarOnShow = false;
        var PlayBarAni = new DoubleAnimation
        {
            To = 0,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var PlayBarTransAni = new DoubleAnimation
        {
            To = 20,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var PlayBarBlurTransAni = new DoubleAnimation
        {
            To = 0,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var PointerOutAni = new Storyboard();
        Storyboard.SetTarget(PlayBarAni, GridPlayBar);
        Storyboard.SetTarget(PlayBarTransAni, PlayBarTrans);
        Storyboard.SetTarget(PlayBarBlurTransAni, GridPlayBarMarginBlur);
        Storyboard.SetTargetProperty(PlayBarAni, "Opacity");
        Storyboard.SetTargetProperty(PlayBarBlurTransAni, "Opacity");
        Storyboard.SetTargetProperty(PlayBarTransAni, "Y");
        PointerOutAni.Children.Add(PlayBarAni);
        PointerOutAni.Children.Add(PlayBarTransAni);
        PointerOutAni.Children.Add(PlayBarBlurTransAni);
        PointerOutAni.Begin();
        return Task.CompletedTask;
    }

    private static void SetPlayBarMarginBlurEffect(UIElement sender)
    {
        var helper = new LinearGradientBlurVisualHelper(Window.Current.Compositor);
        ElementCompositionPreview.SetElementChildVisual(sender, helper.RootVisual);
    }

    internal class LinearGradientBlurVisualHelper
    {
        private readonly Compositor compositor;

        private Color tintColor;
        private const float maxBlurAmount = 64f;
        private readonly SpriteVisual[] visuals;
        private readonly ColorStop[][] colorStops;
        private readonly SpriteVisual rootVisual;
        private readonly SpriteVisual tintColorVisual;

        public LinearGradientBlurVisualHelper(Compositor compositor)
        {
            this.compositor = compositor;

            tintColor = Color.FromArgb(0, 0, 0, 0);

            var dColor = Color.FromArgb(255, 0, 0, 0);
            var hColor = Color.FromArgb(0, 0, 0, 0);

            visuals = new SpriteVisual[8];
            colorStops =
            [
                [(0f, dColor), (0.125f, hColor)],
                [(0f, dColor), (0.125f, dColor), (0.25f, hColor)],
                [(0f, hColor), (0.125f, dColor), (0.25f, dColor), (0.375f, hColor)],
                [(0.125f, dColor), (0.25f, hColor), (0.375f, dColor), (0.5f, hColor)],
                [(0.25f, dColor), (0.375f, hColor), (0.5f, dColor), (0.625f, hColor)],
                [(0.375f, dColor), (0.5f, hColor), (0.625f, dColor), (0.75f, hColor)],
                [(0.5f, dColor), (0.625f, hColor), (0.75f, dColor), (0.875f, hColor)],
                [(0.625f, dColor), (0.75f, hColor), (0.875f, dColor), (1, hColor)],
            ];

            rootVisual = compositor.CreateSpriteVisual();
            rootVisual.RelativeSizeAdjustment = Vector2.One;

            for (int i = 0; i < visuals.Length; i++)
            {
                var blurAmount = maxBlurAmount;
                for (int j = 0; j < i; j++)
                {
                    blurAmount /= 2;
                }
                rootVisual.Children.InsertAtTop(visuals[i] = CreateVisual(compositor, blurAmount, colorStops[i]));
            }

            rootVisual.Children.InsertAtTop(tintColorVisual = CreateTintColorVisual(compositor, tintColor));
        }

        public Visual RootVisual => rootVisual;

        public Color TintColor
        {
            get => tintColor;
            set
            {

                if (tintColor != value)
                {
                    tintColor = value;
                    if (tintColorVisual.Brush is CompositionLinearGradientBrush brush
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
            get => maxBlurAmount;
            set
            {

                if (maxBlurAmount != value)
                {
                    for (int i = 0; i < visuals.Length; i++)
                    {
                        var blurAmount = maxBlurAmount;
                        for (int j = 0; j < i; j++)
                        {
                            blurAmount /= 2;
                        }
                        visuals[i].Brush.Properties.InsertScalar("BlurEffect.BlurAmount", blurAmount);
                    }
                }
            }
        }

        private static Color MakeTransparent(Color color) => Color.FromArgb(0, color.R, color.G, color.B);

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

        private static SpriteVisual CreateVisual(Compositor compositor, float blurAmount, params (float offset, Color color)[] stops)
        {
            var effect = new AlphaMaskEffect()
            {
                AlphaMask = new CompositionEffectSourceParameter("mask"),
                Source = new GaussianBlurEffect()
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

            maskBrush.StartPoint = new System.Numerics.Vector2(0, 1);
            maskBrush.EndPoint = new System.Numerics.Vector2(0, 0);
            maskBrush.MappingMode = CompositionMappingMode.Relative;

            for (int i = 0; i < stops.Length; i++)
            {
                maskBrush.ColorStops.Add(compositor.CreateColorGradientStop(stops[i].offset, stops[i].color));
            }

            brush.SetSourceParameter("source", compositor.CreateBackdropBrush());
            brush.SetSourceParameter("mask", maskBrush);

            var visual = compositor.CreateSpriteVisual();
            visual.RelativeSizeAdjustment = Vector2.One;
            visual.Brush = brush;
            return visual;
        }
    }

    private void Page_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.IsGenerated) return;
        Ioc.Default.GetRequiredService<IPlayBarAutoHideService>().Show();
    }
}
