#region

using System.ComponentModel;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Composition;
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
using HyPlayer.Application.State;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Library;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Playlist;
using HyPlayer.Features.Search;
using HyPlayer.Features.User;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Shell;
using HyPlayer.Shell.ExpandedPlayer;
using HyPlayer.Shell.Navigation.Services;
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
    private bool _playBarAutoHideSubscribed;
    private WeakEventListener<MainPage, object?, PropertyChangedEventArgs>? _surfaceStoreChangedListener;
    private bool _isPlaybarOnShow = true;

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
        SetPlayBarMarginBlurEffect(playBarMarginRect);
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
        GridPlayBar.Background = Windows.UI.Xaml.Application.Current
            .Resources["SystemControlAcrylicElementMediumHighBrush"].As<Brush>();
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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _surfaceStoreChangedListener?.Detach();
        _surfaceStoreChangedListener = null;
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

    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!UISettings.AutoHidePlaybar) return;
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
