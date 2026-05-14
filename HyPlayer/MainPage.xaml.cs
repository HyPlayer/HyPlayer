#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.NeteaseApi;
using HyPlayer.Pages;
using HyPlayer.Services.Playback;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using WinRT;
using ColorStop = (float offset, Windows.UI.Color color);

using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using AsyncAwaitBestPractices;
#endregion

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer;


/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MainPage : Page
{
    bool IsPlaybarOnShow = true;
    public bool IsExpandedPlayerInitialized = false;
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    public MainPage()
    {
        Ioc.Default.GetRequiredService<IUIStateService>().PageMain = this;
        var _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        if (_api != null)
        {
            _api.Option.XRealIP = Setting.GetSettings<string>("xRealIp", null);
            _api.Option.DegradeHttp = Setting.GetSettings("UseHttp", false);
        }
        if (_setting.uiSound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        UIElement PlayBarMarginRect = PlayBarMarginBackground?.As<UIElement>();
        SetPlayBarMarginBlurEffect(PlayBarMarginRect);
        ActualThemeChanged += MainPage_ActualThemeChanged;
        Ioc.Default.GetRequiredService<IUIStateService>().BrushManagement.IsBright = ActualTheme == ElementTheme.Light;
        if (_setting.displayMaintain)
        {
            // displayRequest
            Ioc.Default.GetRequiredService<IUIStateService>().DisplayRequest.RequestActive();
        }
    }

    private void MainPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        _setting.OnPropertyChanged("acrylicBackgroundStatus");
        _setting.OnPropertyChanged("playbarBackgroundAcrylic");
        if (!Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded) Ioc.Default.GetRequiredService<IUIStateService>().BrushManagement.IsBright = ActualTheme == ElementTheme.Light;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ActualThemeChanged -= MainPage_ActualThemeChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ApplicationView.GetForCurrentView().IsFullScreenMode)
        {
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        }
        switch (e.Parameter)
        {
            case "search":
                Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Search));
                break;
            case "account":
                Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Me));
                break;
            case "likedsongs":
                Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(SongListDetail), Ioc.Default.GetRequiredService<IAuthService>().MySongLists[0].PlaylistId);
                break;
            case "local":
                Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(LocalMusicPage));
                break;
        }
    }
    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!_setting.AutoHidePlaybar) return;
        _ = Ioc.Default.GetRequiredService<INotificationService>().InvokeOnUIThread(() =>
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

    private void ShowBar()
    {
        (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemBlank.IsEnabled = false;
        if (!IsPlaybarOnShow)
        {
            PointerInAni.Begin();
            (Ioc.Default.GetRequiredService<IUIStateService>().BarPlayBar as PlayBar).RefreshPlayBarCover(Ioc.Default.GetRequiredService<PlaybackStateService>().NowPlayingItem);
            var BlankAni = new DoubleAnimation
            {
                To = 0,
                EnableDependentAnimation = true,
                EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(BlankAni, (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemBlank);
            Storyboard.SetTargetProperty(BlankAni, "Opacity");
            storyboard.Children.Add(BlankAni);
            storyboard.Begin();
        }

    }

    private async Task CollapseBar()
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
        (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemBlank.IsEnabled = true;
        var BlankAni = new DoubleAnimation
        {
            To = 1,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(BlankAni, (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemBlank);
        Storyboard.SetTargetProperty(BlankAni, "Opacity");
        storyboard.Children.Add(BlankAni);
        storyboard.Begin();
        await (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).RefreshNavItemCover(Ioc.Default.GetRequiredService<PlaybackStateService>().NowPlayingItem);
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
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        uiState.PlaybarSecondCounter = 0;
        if (!uiState.PlaybarIsVisible)
        {
            uiState.InvokePlaybarVisibilityChanged(true);
            uiState.PlaybarIsVisible = true;
        }
    }
}
