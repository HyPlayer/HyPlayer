#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using System;
using System.Net;
using System.Threading.Tasks;
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
    bool IsPlaybarOnShow=true;
    public bool IsExpandedPlayerInitialized=false;
    public MainPage()
    {
        Common.PageMain = this;
        Common.ncapi.RealIP = Setting.GetSettings<string>("xRealIp", null);
        Common.ncapi.Proxy = new WebProxy(Setting.GetSettings<string>("neteaseProxy", null));
        Common.ncapi.UseProxy = !(ApplicationData.Current.LocalSettings.Values["neteaseProxy"] is null);
        Common.ncapi.UseHttp = Setting.GetSettings<bool>("UseHttp", false);
        StaticSource.PICSIZE_AUDIO_PLAYER_COVER = Common.Setting.highQualityCoverInSMTC ? "1024y1024" : "100y100";
        if (Common.Setting.uiSound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        _ = HyPlayList.OnAudioRenderDeviceChangedOrInitialized();
        ActualThemeChanged += MainPage_ActualThemeChanged;
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
        (GridPlayBar.Children[0] as PlayBar).RefreshSongList();
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

    private void PointerIn(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Common.Setting.AutoHidePlaybar)
        {
            Common.PageBase.NavItemBlank.IsEnabled = false;
            if (IsPlaybarOnShow)
            { }
            else
            {
                if (IsExpandedPlayerInitialized)
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
                }
                else
                {
                    PointerInAni.Begin();
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
        }
    }

    private void PointerOut(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Common.Setting.AutoHidePlaybar)
        {

            IsPlaybarOnShow = false;
            if (IsExpandedPlayerInitialized)
            {
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
            }
            else
            {
                PointerOutAni.Begin();
                Common.PageBase.NavItemBlank.IsEnabled = true;
                var BlankAni = new DoubleAnimation
                {
                    BeginTime = TimeSpan.FromSeconds(3.1),
                    To = 1,
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
    }
}