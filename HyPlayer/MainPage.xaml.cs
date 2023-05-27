#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using System;
using System.Net;
using Windows.Storage;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer;


/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MainPage
{
    bool IsPlaybarOnShow = true;
    public bool IsExpandedPlayerInitialized = false;
    public MainPage()
    {
        Common.PageMain = this;
        Common.ncapi.RealIP = Setting.GetSettings<string>("xRealIp", null);
        Common.ncapi.Proxy = new WebProxy(Setting.GetSettings<string>("neteaseProxy", null));
        Common.ncapi.UseProxy = !(ApplicationData.Current.LocalSettings.Values["neteaseProxy"] is null);
        Common.ncapi.UseHttp = Setting.GetSettings<bool>("UseHttp", false);
        StaticSource.PICSIZE_AUDIO_PLAYER_COVER = Common.Setting.highQualityCoverInSMTC ? "1024y1024" : "640x640";
        if (Common.Setting.uiSound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        _ = HyPlayList.OnAudioRenderDeviceChangedOrInitialized();
        ActualThemeChanged += MainPage_ActualThemeChanged;
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
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
                Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid);
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
            CollapseBar(3);
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
            Common.BarPlayBar.RefreshPlayBarCover();
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

    private void CollapseBar(double time)
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
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
        };
        var PlayBarTransAni = new DoubleAnimation
        {
            BeginTime = TimeSpan.FromSeconds(time),
            To = 20,
            EnableDependentAnimation = true,
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
        };
        var PointerOutAni = new Storyboard();
        Storyboard.SetTarget(PlayBarAni, GridPlayBar);
        Storyboard.SetTarget(PlayBarTransAni, PlayBarTrans);
        Storyboard.SetTargetProperty(PlayBarAni, "Opacity");
        Storyboard.SetTargetProperty(PlayBarTransAni, "Y");
        PointerOutAni.Children.Add(PlayBarAni);
        PointerOutAni.Children.Add(PlayBarTransAni);
        PointerOutAni.Begin();
        Common.PageBase.NavItemBlank.IsEnabled = true;
        var BlankAni = new DoubleAnimation
        {
            BeginTime = TimeSpan.FromSeconds(time),
            To = 1,
            EnableDependentAnimation = true,
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(BlankAni, Common.PageBase.NavItemBlank);
        Storyboard.SetTargetProperty(BlankAni, "Opacity");
        storyboard.Children.Add(BlankAni);
        storyboard.Begin();
        Common.PageBase.RefreshNavItemCover(3);

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
}