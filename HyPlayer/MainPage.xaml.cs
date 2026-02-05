#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
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
    public bool IsExpandedPlayerInitialized = false;
    public MainPage()
    {
        Common.PageMain = this;
        if (Common.NeteaseAPI != null)
        {
            Common.NeteaseAPI.Option.XRealIP = Setting.GetSettings<string>("xRealIp", null);
            Common.NeteaseAPI.Option.DegradeHttp = Setting.GetSettings("UseHttp", false);
        }
        StaticSource.PICSIZE_AUDIO_PLAYER_COVER = Common.Setting.highQualityCoverInSMTC ? "1024y1024" : "640y640";
        if (Common.Setting.uiSound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        UIElement PlayBarMarginRect = PlayBarMarginBackground as UIElement;
        SetPlayBarMarginBlurEffect(PlayBarMarginRect);
        ActualThemeChanged += MainPage_ActualThemeChanged;
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
        if (Common.Setting.displayMaintain)
        {
            // displayRequest
            Common.DisplayRequest.RequestActive();
        }
    }

    private void MainPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        Common.Setting.OnPropertyChanged("acrylicBackgroundStatus");
        Common.Setting.OnPropertyChanged("playbarBackgroundAcrylic");
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
                Common.NavigatePage(typeof(Search));
                break;
            case "account":
                Common.NavigatePage(typeof(Me));
                break;
            case "likedsongs":
                Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].PlaylistId);
                break;
            case "local":
                Common.NavigatePage(typeof(LocalMusicPage));
                break;
        }
    }
    private void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!Common.Setting.AutoHidePlaybar) return;
        if (isActivated)
        {
            ShowBar();
        }
        else
        {
            _ = CollapseBar(3);
        }
    }

    private void ShowBar()
    {
        Common.PageBase.NavItemBlank.IsEnabled = false;
        if (IsPlaybarOnShow)
        { }
        else
        {

            //var ExpandedPlayerLyricAni = new DoubleAnimation
            //{
            //    BeginTime = TimeSpan.FromSeconds(3.1),
            //    To = 0,
            //    EnableDependentAnimation = true,
            //    EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            //};
            //Storyboard.SetTarget(ExpandedPlayerLyricAni, Common.PageExpandedPlayer.LyricBoxContainer);
            //Storyboard.SetTargetProperty(ExpandedPlayerLyricAni, "(FrameworkElement.MarginProperty).Bottom");
            //var lyricstoryboard = new Storyboard();
            //lyricstoryboard.Children.Add(ExpandedPlayerLyricAni);
            //lyricstoryboard.Begin();

            PointerInAni.Begin();
            Common.BarPlayBar.RefreshPlayBarCover(HyPlayList.NowPlayingItem);
            var BlankAni = new DoubleAnimation
            {
                To = 0,
                EnableDependentAnimation = true,
                EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(BlankAni, Common.PageBase.NavItemBlank);
            Storyboard.SetTargetProperty(BlankAni, "Opacity");
            storyboard.Children.Add(BlankAni);
            storyboard.Begin();
        }

    }

    private async Task CollapseBar(double time)
    {
        IsPlaybarOnShow = false;

        //var ExpandedPlayerLyricAni = new DoubleAnimation
        //{
        //    BeginTime = TimeSpan.FromSeconds(3.1),
        //    To = -140,
        //    EnableDependentAnimation = true,
        //    EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
        //};
        //var lyricstoryboard = new Storyboard();
        //Storyboard.SetTarget(ExpandedPlayerLyricAni, Common.PageExpandedPlayer.LyricBoxContainer);
        //Storyboard.SetTargetProperty(ExpandedPlayerLyricAni, "(FrameworkElement.MarginProperty).Bottom");
        //lyricstoryboard.Children.Add(ExpandedPlayerLyricAni);
        //lyricstoryboard.Begin();
        var PlayBarAni = new DoubleAnimation
        {
            BeginTime = TimeSpan.FromSeconds(time),
            To = 0,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var PlayBarTransAni = new DoubleAnimation
        {
            BeginTime = TimeSpan.FromSeconds(time),
            To = 20,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var PlayBarBlurTransAni = new DoubleAnimation
        {
            BeginTime = TimeSpan.FromSeconds(time),
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
        Common.PageBase.NavItemBlank.IsEnabled = true;
        var BlankAni = new DoubleAnimation
        {
            BeginTime = TimeSpan.FromSeconds(time),
            To = 1,
            EnableDependentAnimation = true,
            EasingFunction = new PowerEase() { EasingMode = EasingMode.EaseInOut },
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(BlankAni, Common.PageBase.NavItemBlank);
        Storyboard.SetTargetProperty(BlankAni, "Opacity");
        storyboard.Children.Add(BlankAni);
        storyboard.Begin();
        await Common.PageBase.RefreshNavItemCover(3, HyPlayList.NowPlayingItem);

    }
    private void Page_PointerEntered(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Common.PointerIsInMainPage = true;
        Common.PlaybarSecondCounter = 0;
        if (!Common.PlaybarIsVisible)
        {
            Common.OnPlaybarVisibilityChanged?.Invoke(true);
            Common.PlaybarIsVisible = true;
        }
    }
    private void Page_PointerExited(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Common.PointerIsInMainPage = false;
    }

    private void SetPlayBarMarginBlurEffect(UIElement sender)
    {
        var helper = new LinearGradientBlurVisualHelper(Window.Current.Compositor);
        ElementCompositionPreview.SetElementChildVisual(sender, helper.RootVisual);
    }

    internal class LinearGradientBlurVisualHelper
    {
        private readonly Compositor compositor;

        private Color tintColor;
        private float maxBlurAmount = 64f;
        private SpriteVisual[] visuals;
        private ColorStop[][] colorStops;
        private SpriteVisual rootVisual;
        private SpriteVisual tintColorVisual;

        public LinearGradientBlurVisualHelper(Compositor compositor)
        {
            this.compositor = compositor;

            tintColor = Color.FromArgb(0, 0, 0, 0);

            var dColor = Color.FromArgb(255, 0, 0, 0);
            var hColor = Color.FromArgb(0, 0, 0, 0);

            visuals = new SpriteVisual[8];
            colorStops = new[]
            {
                new []{ (0f, dColor), (0.125f, hColor) },
                new []{ (0f, dColor), (0.125f, dColor), (0.25f, hColor) },
                new []{ (0f, hColor), (0.125f, dColor), (0.25f, dColor), (0.375f, hColor) },
                new []{ (0.125f, dColor), (0.25f, hColor), (0.375f, dColor), (0.5f, hColor) },
                new []{ (0.25f, dColor), (0.375f, hColor), (0.5f, dColor), (0.625f, hColor) },
                new []{ (0.375f, dColor), (0.5f, hColor), (0.625f, dColor), (0.75f, hColor) },
                new []{ (0.5f, dColor), (0.625f, hColor), (0.75f, dColor), (0.875f, hColor) },
                new []{ (0.625f, dColor), (0.75f, hColor), (0.875f, dColor), (1, hColor) },
            };

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

            var brush = compositor.CreateEffectFactory(effect, new[] { "BlurEffect.BlurAmount" })
                .CreateBrush();

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
}