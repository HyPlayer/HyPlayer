#region

#nullable enable
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.LyricRenderer.Converters;
using HyPlayer.LyricRenderer.RollingCalculators;
using Microsoft.Toolkit.Uwp.UI.Animations;
using Microsoft.Toolkit.Uwp.UI.Media;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using HyPlayer.LyricRenderer;
using Buffer = Windows.Storage.Streams.Buffer;
using Color = System.Drawing.Color;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ExpandedPlayer : Page, IDisposable
{
    public static readonly DependencyProperty NowPlaybackSpeedProperty = DependencyProperty.Register(
        "NowPlaybackSpeed", typeof(string), typeof(ExpandedPlayer),
        new PropertyMetadata("x" + HyPlayList.Player.PlaybackSession.PlaybackRate));

    public SolidColorBrush ForegroundAccentTextBrush =
        Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush;

    public SolidColorBrush ForegroundIdleTextBrush =
        Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush;


    public bool jumpedLyrics;
    public double lastChangedLyricWidth;
    private int lastheight;
    private bool _lyricHasBeenLoaded = false;
    private bool _lyricIsReadyToGo = false;
    private bool _lyricIsCleaning = false;
    private bool _lyricColorLoaded = false;
    private bool _lyricCoverChanged = false;

    private LyricItemModel lastitem;
    private int lastlrcid;

    public PlayItem lastSongForBrush;

    private int lastwidth;

    public ObservableCollection<LyricItemModel> LyricList = new();
    private bool ManualChangeMode;
    private int needRedesign = 1;
    private int nowheight;
    private int nowwidth;
    private int offset;
    private bool programClick;
    private bool realclick;
    private int sclock;
    private ExpandedWindowMode WindowMode;
    private AppWindow expandedPlayerWindow;
    public Windows.UI.Color? albumMainColor;
    private bool disposedValue;
    public System.Diagnostics.Stopwatch time = new System.Diagnostics.Stopwatch();


    public ExpandedPlayer()
    {
        InitializeComponent();
        Common.PageExpandedPlayer = this;
        HyPlayList.OnPause += HyPlayList_OnPause;
        HyPlayList.OnPlay += HyPlayList_OnPlay;
        HyPlayList.OnPlayItemChange += OnSongChange;
        HyPlayList.OnSongCoverChanged += RefreshAlbumCover;
        HyPlayList.OnLyricLoaded += HyPlayList_OnLyricLoaded;
        Window.Current.SizeChanged += Current_SizeChanged;
        HyPlayList.OnTimerTicked += HyPlayList_OnTimerTicked;
        Common.OnEnterForegroundFromBackground += OnEnteringForeground;
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
        LyricBox.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        LyricBox.OnBeforeRender += LyricBox_OnBeforeRender;
        LyricBox.OnRequestSeek += LyricBoxOnOnRequestSeek;
        LyricBox.Context.LyricWidthRatio = 1;
        LyricBox.Context.LyricPaddingTopRatio = 0.1;
        LyricBox.Context.CurrentLyricTime = 0;
    }

    private void LyricBoxOnOnRequestSeek(long time)
    {
        HyPlayList.Player.PlaybackSession.Position = TimeSpan.FromMilliseconds(time);
    }

    private void LyricBox_OnBeforeRender(LyricRenderer.LyricRenderView view)
    {
        if (HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds < view.Context.CurrentLyricTime)
        {
            view.Context.CurrentLyricTime = (long)HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
            LyricBox.ReflowTime(0);
        }
        else
        {
            view.Context.CurrentLyricTime = (long)HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
        }

    }

    public double showsize { get; set; }
    public double LyricWidth { get; set; }

    public string NowPlaybackSpeed
    {
        get => (string)GetValue(NowPlaybackSpeedProperty);
        set => SetValue(NowPlaybackSpeedProperty, value);
    }

    private void HyPlayList_OnPlay()
    {
        _ = Common.Invoke(() =>
        {
            if (!Common.IsInImmersiveMode)
            {
                if (Common.Setting.albumRotate)
                    //网易云音乐圆形唱片
                    RotateAnimationSet.StartAsync();
                if (Common.Setting.expandAlbumBreath)
                {
                    ImageAlbumAni.Begin();
                }
            }

            if (bpmAniStoryboard.Children.Count > 0)
            {
                bpmAniStoryboard.Resume();
            }

            if (luminousColorsRotateStoryBoard.Children.Count > 0)
            {
                luminousColorsRotateStoryBoard.Resume();
            }
        });
    }

    private void HyPlayList_OnPause()
    {
        _ = Common.Invoke(() =>
        {
            if (Common.Setting.albumRotate)
                RotateAnimationSet.Stop();
            if (Common.Setting.expandAlbumBreath)
            {
                ImageAlbumAni.Pause();
            }

            if (bpmAniStoryboard.Children.Count > 0)
            {
                bpmAniStoryboard.Pause();
            }

            if (luminousColorsRotateStoryBoard.Children.Count > 0)
            {
                luminousColorsRotateStoryBoard.Pause();
            }
        });
    }

    private void HyPlayList_OnTimerTicked()
    {
        if (Common.IsInBackground) return;
        if (sclock > 0) sclock--;
        if (needRedesign > 0)
        {
            needRedesign--;
            Redesign();
        }
    }


    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        Dispose();
    }

    private void HyPlayList_OnLyricLoaded()
    {
        LoadLyricsBox();
        needRedesign++;
    }

    private void Current_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        nowwidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
        nowheight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
        if (lastwidth != nowwidth)
        {
            //这段不要放出去了
            if (nowwidth > 800 || WindowMode == ExpandedWindowMode.Both)
                LyricWidth = nowwidth * 0.4;
            else
                LyricWidth = nowwidth - 15;
            LyricWidth = Math.Max(LyricWidth, 0);
            showsize = Common.Setting.lyricSize <= 0
                ? Math.Max(nowwidth / 66, 23)
                : Common.Setting.lyricSize;

            lastwidth = nowwidth;
            needRedesign += 2;
        }
        else if (lastheight != nowheight)
        {
            lastheight = nowheight;
            needRedesign += 2;
        }
    }

    private void ChangeWindowMode()
    {
        realclick = false;
        switch (WindowMode)
        {
            case ExpandedWindowMode.Both:
                BtnToggleAlbum.IsChecked = true;
                BtnToggleLyric.IsChecked = true;
                RightPanel.Visibility = Visibility.Visible;
                UIAugmentationSys.Visibility = Visibility.Visible;
                LyricBox.Margin = new Thickness(0);
                UIAugmentationSys.SetValue(Grid.ColumnProperty, 0);
                UIAugmentationSys.SetValue(Grid.ColumnSpanProperty, 1);
                RightPanel.SetValue(Grid.ColumnProperty, 1);
                RightPanel.SetValue(Grid.ColumnSpanProperty, 1);
                break;
            case ExpandedWindowMode.CoverOnly:
                BtnToggleAlbum.IsChecked = true;
                BtnToggleLyric.IsChecked = false;
                UIAugmentationSys.Visibility = Visibility.Visible;
                RightPanel.Visibility = Visibility.Collapsed;
                UIAugmentationSys.SetValue(Grid.ColumnProperty, 0);
                UIAugmentationSys.SetValue(Grid.ColumnSpanProperty, 2);
                UIAugmentationSys.VerticalAlignment = VerticalAlignment.Stretch;
                UIAugmentationSys.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
            case ExpandedWindowMode.LyricOnly:
                BtnToggleAlbum.IsChecked = false;
                BtnToggleLyric.IsChecked = true;
                RightPanel.Visibility = Visibility.Visible;
                UIAugmentationSys.Visibility = Visibility.Collapsed;
                RightPanel.SetValue(Grid.ColumnProperty, 0);
                RightPanel.SetValue(Grid.ColumnSpanProperty, 2);
                LyricBox.Margin = new Thickness(15);
                break;
        }

        if (WindowMode == ExpandedWindowMode.LyricOnly)
            LyricWidth = nowwidth - 30;
        else
        {
            if (nowwidth > 800 || WindowMode == ExpandedWindowMode.Both)
                LyricWidth = nowwidth * 0.4;
            else
                LyricWidth = nowwidth - 30;
        }

        needRedesign++;
        realclick = true;
    }

    private void Redesign()
    {
        if (needRedesign > 5) needRedesign = 5;
        // 这个函数里面放无法用XAML实现的页面布局方式


        if (600 > Math.Min(LeftPanel.ActualHeight, MainGrid.ActualHeight))
        {
            /*
            ImageAlbum.Width = Math.Max(Math.Min(MainGrid.ActualHeight, LeftPanel.ActualWidth) - 80, 1);
            ImageAlbum.Height = ImageAlbum.Width;
            */

            /*
            if (ImageAlbum.Width < 250 || iscompact)
                SongInfo.Visibility = Visibility.Collapsed;
            else
                SongInfo.Visibility = Visibility.Visible;
            */
            SongInfo.Width = ImageAlbum.Width;
        }
        else
        {
            ImageAlbum.Width = double.NaN;
            ImageAlbum.Height = double.NaN;
            SongInfo.Width = double.NaN;
        }

        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;

        /*
        if (550 > nowwidth)
        {
            ImageAlbum.Width = nowwidth - ImageAlbum.ActualOffset.X - 15;
            LeftPanel.HorizontalAlignment = HorizontalAlignment.Left;
            ImageAlbum.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            ImageAlbum.Width = double.NaN;
            LeftPanel.HorizontalAlignment = HorizontalAlignment.Center;
            ImageAlbum.HorizontalAlignment = HorizontalAlignment.Center;
        }
        */

        float sizey = 1;
        float sizex = 1;
        if (WindowMode != ExpandedWindowMode.LyricOnly)
        {
            if (SongInfo.ActualOffset.Y + SongInfo.ActualHeight > MainGrid.ActualHeight)
                sizey = (float)(MainGrid.ActualHeight / (SongInfo.ActualOffset.Y + SongInfo.ActualHeight));

            if (ImageAlbum.ActualOffset.X + ImageAlbum.ActualWidth > LeftPanel.ActualWidth)
                sizex = (float)(LeftPanel.ActualWidth / (ImageAlbum.ActualOffset.X + ImageAlbum.ActualWidth));
            UIAugmentationSys.ChangeView(0, 0, Math.Min(sizex, sizey));
        }
        //{//合并显示
        //    SongInfo.SetValue(Grid.RowProperty, 1);
        //    SongInfo.VerticalAlignment = VerticalAlignment.Bottom;
        //    SongInfo.Background = Application.Current.Resources["ExpandedPlayerMask"] as Brush;
        //}
        //else
        //{
        //    SongInfo.SetValue(Grid.RowProperty, 2);
        //    SongInfo.VerticalAlignment = VerticalAlignment.Top;
        //    SongInfo.Background = null;
        //}

        /*
         // 小窗下的背景替换
        if ((nowwidth <= 300 || nowheight <= 300) && iscompact && StackPanelTiny.Visibility == Visibility.Collapsed)
        {
            ImageAlbum.Visibility = Visibility.Collapsed;
            PageContainer.Background = null;
        }
        else
        {
            ImageAlbum.Visibility = Visibility.Visible;
            PageContainer.Background = Application.Current.Resources["ExpandedPlayerMask"] as AcrylicBrush;
        }
        */
        lastChangedLyricWidth = LyricWidth;

        //歌词宽度
        if (nowwidth <= 800)
        {
            if (!ManualChangeMode && WindowMode == ExpandedWindowMode.Both)
            {
                WindowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
            }
        }
        else if (nowwidth > 800)
        {
            if (!ManualChangeMode && WindowMode != ExpandedWindowMode.Both)
            {
                WindowMode = ExpandedWindowMode.Both;
                ChangeWindowMode();
            }
        }

        LyricBox.Width = LyricWidth;
        LyricBox.ChangeRenderFontSize(showsize, (Common.Setting.translationSize > 0) ? Common.Setting.translationSize : showsize / 2, Common.Setting.romajiSize);

        ImageRotateTransform.CenterX = ImageAlbum.ActualSize.X / 2;
        ImageRotateTransform.CenterY = ImageAlbum.ActualSize.Y / 2;

        BgScale.CenterY = LuminousBackgroundContainer.ActualHeight / 2;
        BgScale.CenterX = LuminousBackgroundContainer.ActualWidth / 2;

        BgRotate.CenterX = LuminousBackgroundContainer.ActualWidth / 2;
        BgRotate.CenterY = LuminousBackgroundContainer.ActualHeight / 2;
    }


    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay)
            await ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
        if (ApplicationView.GetForCurrentView().IsFullScreenMode)
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        Common.PageExpandedPlayer = null;
    }

    private Storyboard luminousColorsRotateStoryBoard = new Storyboard();
    private DoubleAnimation luminousColorsRotateAnimation = new DoubleAnimation();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Common.IsInBackground = false;
        Common.PageExpandedPlayer = this;
        Common.IsInImmersiveMode = false;
        if (e.Parameter is null || (bool)e.Parameter)
            Window.Current.SetTitleBar(AppTitleBar);

        Current_SizeChanged(null, null);
        Redesign();
        //LeftPanel.Visibility = Visibility.Collapsed;
        programClick = true;
        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;
        programClick = false;
        try
        {
            OnSongChange(HyPlayList.List[HyPlayList.NowPlaying]);
            using var coverStream = HyPlayList.CoverStream.CloneStream();
            await RefreshAlbumCover(HyPlayList.NowPlayingHashCode, coverStream);
            ChangeWindowMode();
            needRedesign++;
        }
        catch
        {
        }

        if (Common.Setting.expandedPlayerBackgroundType == 0 && !Common.Setting.expandedUseAcrylic)
            AcrylicCover.Fill = new BackdropBlurBrush { Amount = 50.0 };
        if (Common.Setting.expandedPlayerBackgroundType == 6)
        {
            AcrylicCover.Fill = new BackdropBlurBrush { Amount = 250 }; // TintAmountChange
            luminousColorsRotateAnimation = BgRotate.CreateDoubleAnimation(
                "Angle",
                360,
                0,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(12),
                repeatBehavior: RepeatBehavior.Forever,
                autoReverse: false);
            luminousColorsRotateStoryBoard.Children.Add(luminousColorsRotateAnimation);
            luminousColorsRotateStoryBoard.Begin();
        }

        if (Common.Setting.expandedPlayerBackgroundType == 5)
            PageContainer.Background =
                (Brush)new BooleanToWindowBrushesConverter().Convert(
                    Common.Setting.acrylicBackgroundStatus, null, null,
                    null);

        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private readonly BringIntoViewOptions AnimatedBringIntoViewOptions =
        new BringIntoViewOptions()
        {
            VerticalAlignmentRatio = 0.5,
            AnimationDesired = true,
        };

    private readonly BringIntoViewOptions NoAnimationBringIntoViewOptions =
        new BringIntoViewOptions()
        {
            VerticalAlignmentRatio = 0.5,
            AnimationDesired = false,
        };

    private Storyboard bpmAniStoryboard = new Storyboard();

    public async void InitializeBPM()
    {
        bpmAniStoryboard.Stop();
        if (Common.ncapi is null || HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Netease ||
            string.IsNullOrEmpty(HyPlayList.NowPlayingItem.PlayItem.Id))
            return;
        var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongWikiSummary,
            new()
            {
                { "id", HyPlayList.NowPlayingItem.PlayItem.Id }
            });
        if (json["code"]?.ToString() != "200") return;
        // 寻找 BPM 的 Node
        var blocks = json["data"]?["blocks"]?.ToArray();
        if (blocks is null) return;
        foreach (var block in blocks)
        {
            if (block["code"]?.ToString() != "SONG_PLAY_ABOUT_SONG_BASIC") continue;
            var creatives = block["creatives"]?.ToArray();
            if (creatives is null) continue;
            foreach (var creative in creatives)
            {
                if (creative["creativeType"]?.ToString() != "bpm") continue;
                var bpmText = creative["uiElement"]?["textLinks"]?[0]?["text"]?.ToString();
                if (string.IsNullOrEmpty(bpmText)) continue;
                if (double.TryParse(bpmText, out var bpm))
                {
                    var animationX = BgScale.CreateDoubleAnimation(
                        "ScaleX",
                        1.8,
                        1,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(60 * (Common.Setting.gentleBPMAnimation ? 10 : 1) / bpm),
                        repeatBehavior: RepeatBehavior.Forever,
                        autoReverse: true,
                        easing: Common.Setting.gentleBPMAnimation
                            ? new BackEase { EasingMode = EasingMode.EaseInOut }
                            : null);
                    var animationY = BgScale.CreateDoubleAnimation(
                        "ScaleY",
                        1.8,
                        1,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(60 * (Common.Setting.gentleBPMAnimation ? 10 : 1) / bpm),
                        repeatBehavior: RepeatBehavior.Forever,
                        autoReverse: true,
                        easing: Common.Setting.gentleBPMAnimation
                            ? new BackEase { EasingMode = EasingMode.EaseInOut }
                            : null);
                    bpmAniStoryboard.Children.Clear();
                    bpmAniStoryboard.Children.Add(animationX);
                    bpmAniStoryboard.Children.Add(animationY);
                    bpmAniStoryboard.Begin();
                    luminousColorsRotateAnimation.Duration = TimeSpan.FromSeconds(3200 / bpm);
                    LyricBox.ChangeBeatPerMinute(bpm);
                }
            }
        }
    }

    public void LoadLyricsBox()
    {
        _ = Common.Invoke(() =>
        {
            _lyricIsReadyToGo = true;
            if (_lyricIsCleaning) return;
            LyricBox.SetLyricLines(LrcConverter.Convert(HyPlayList.Lyrics));
            LyricBox.ChangeAlignment(Common.Setting.lyricAlignment  switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left
            });
            LyricBox.ReflowTime(0);
            lastlrcid = HyPlayList.NowPlayingHashCode;
            if (HyPlayList.NowPlayingItem == null) return;
            LyricBox.Width = LyricWidth;
            LyricBox.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color);
            LyricBox.ChangeRenderFontSize(showsize, (Common.Setting.translationSize > 0 ) ? Common.Setting.translationSize : showsize / 2, Common.Setting.romajiSize);
        });
    }

    private Windows.UI.Color GetKaraokAccentBrush()
    {
        if (Common.Setting.karaokLyricFocusingColor is not null)
        {
            _karaokAccentColorCache ??= Common.Setting.karaokLyricFocusingColor;
            return _karaokAccentColorCache.Value;
        }

        return Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundAccentTextBrush.Color
            : (Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush)!.Color;
    }

    private SolidColorBrush GetAccentBrush()
    {
        if (Common.Setting.pureLyricFocusingColor is not null)
        {
            return _pureAccentBrushCache ??= new SolidColorBrush(Common.Setting.pureLyricFocusingColor.Value);
        }

        return (Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundAccentTextBrush
            : Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush)!;
    }

    private SolidColorBrush GetIdleBrush()
    {
        if (Common.Setting.pureLyricIdleColor is not null)
        {
            return _pureIdleBrushCache ??= new SolidColorBrush(Common.Setting.pureLyricIdleColor.Value);
        }

        return (Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundIdleTextBrush
            : Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush)!;
    }

    public async Task OnEnteringForeground()
    {
        OnSongChange(HyPlayList.NowPlayingItem);
        using var coverStream = HyPlayList.CoverStream.CloneStream();
        await RefreshAlbumCover(HyPlayList.NowPlayingHashCode, coverStream);
        if (!_lyricHasBeenLoaded) HyPlayList_OnLyricLoaded();
    }

    public void OnSongChange(HyPlayItem mpi)
    {
        var lyricIsReady = lastlrcid == HyPlayList.NowPlayingItem.GetHashCode();
        _lyricIsReadyToGo = lyricIsReady;
        _lyricHasBeenLoaded = lyricIsReady;
        _lyricCoverChanged = !lyricIsReady;
        _ = Common.Invoke(() =>
        {
            TextBlockSinger.Content = mpi?.PlayItem?.ArtistString;
            TextBlockSongTitle.Text = mpi?.PlayItem?.Name;
            TextBlockAlbum.Content = mpi?.PlayItem?.AlbumString;
            if (mpi?.PlayItem == null)
            {
                LyricList.Clear();
                //LyricBox.Children.Clear();

                //LyricBox.Children.Add(new TextBlock() { Text = "当前暂无歌曲播放" });
                ImageAlbum.Source = null;
            }

            if (mpi?.PlayItem == null) return;

            if (!lyricIsReady)
            {
                if (!_lyricIsReadyToGo)
                {
                    //歌词加载中提示
                    _lyricIsCleaning = true;
                    lock (LyricList)
                    {
                        LyricList.Clear();
                        LyricList.Add(new LyricItemModel(SongLyric.LoadingLyric));
                    }

                    LyricBox.Width = LyricWidth;
                    _lyricIsCleaning = false;
                    if (_lyricIsReadyToGo)
                    {
                        LoadLyricsBox();
                    }
                }
            }

            needRedesign++;
            if (Common.Setting.animationAdaptBPM)
                InitializeBPM();
        });
    }

    public void RefreshLyricColor()
    {
        LyricBox.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color);
    }

    public void StartExpandAnimation()
    {
        ImageAlbum.Visibility = Visibility.Visible;
        TextBlockSinger.Visibility = Visibility.Visible;
        TextBlockSongTitle.Visibility = Visibility.Visible;
        var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
        var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
        var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
        if (anim2 != null) anim3.Configuration = new DirectConnectedAnimationConfiguration();
        if (anim2 != null) anim2.Configuration = new DirectConnectedAnimationConfiguration();
        if (anim2 != null) anim1.Configuration = new DirectConnectedAnimationConfiguration();
        try
        {
            //anim3?.TryStart(TextBlockSinger);
            anim1?.TryStart(TextBlockSongTitle);
            anim2?.TryStart(ImageAlbum);
        }
        catch
        {
            //ignore
        }
    }

    public void StartCollapseAnimation()
    {
        try
        {
            if (Common.Setting.expandAnimation &&
                Common.BarPlayBar.GridSongInfoContainer.Visibility == Visibility.Visible)
            {
                if (TextBlockSongTitle.ActualSize.X != 0 && TextBlockSongTitle.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
                if (ImageAlbum.ActualSize.X != 0 && ImageAlbum.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);
                if (TextBlockSinger.ActualSize.X != 0 && TextBlockSinger.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
                if (TextBlockAlbum.ActualSize.X != 0 && TextBlockAlbum.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongAlbum", TextBlockAlbum);
            }
        }
        catch
        {
            //ignore
        }
    }

    private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        sclock = 5;
    }

    private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.IsPlaying)
            HyPlayList.Player.Pause();
        else
            HyPlayList.Player.Play();
    }

    private void ToggleButtonTranslation_OnClick(object sender, RoutedEventArgs e)
    {
        Common.ShowLyricTrans = ToggleButtonTranslation.IsChecked;
        LoadLyricsBox();
    }

    private void ToggleButtonSound_OnClick(object sender, RoutedEventArgs e)
    {
        Common.ShowLyricSound = ToggleButtonSound.IsChecked;
        LoadLyricsBox();
    }

    private async void TextBlockAlbum_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
                if (HyPlayList.NowPlayingItem.PlayItem.Album.id != "0")
                    Common.NavigatePage(typeof(AlbumPage),
                        HyPlayList.NowPlayingItem.PlayItem.Album.id);

            if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                Common.NavigatePage(typeof(RadioPage), HyPlayList.NowPlayingItem.PlayItem.Album.id);

            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            await Common.BarPlayBar.CollapseExpandedPlayer();
        }
        catch
        {
        }
    }

    private async void TextBlockSinger_OnTapped(object sender, RoutedEventArgs tappedRoutedEventArgs)
    {
        try
        {
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (HyPlayList.NowPlayingItem.PlayItem.Artist.Count > 1)
                {
                    await new ArtistSelectDialog(HyPlayList.NowPlayingItem.PlayItem.Artist).ShowAsync();
                    return;
                }

                Common.NavigatePage(typeof(ArtistPage),
                    HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
            }

            if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);

            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            await Common.BarPlayBar.CollapseExpandedPlayer();
        }
        catch
        {
        }
    }


    private async void SaveAlbumImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var filepicker = new FileSavePicker();
            filepicker.SuggestedFileName = HyPlayList.NowPlayingItem.PlayItem.Name + "-cover.jpg";
            filepicker.FileTypeChoices.Add("图片文件", new List<string> { ".png", ".jpg" });
            var file = await filepicker.PickSaveFileAsync();
            if (file == null) return;
            if (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Local ||
                HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.LocalProgressive)
            {
                using var coverResult =
                    await Common.HttpClient.GetAsync(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover));
                if (coverResult.IsSuccessStatusCode)
                {
                    var cover = await coverResult.Content.ReadAsBufferAsync();
                    await FileIO.WriteBufferAsync(file, cover);
                }
                else
                {
                    Common.AddToTeachingTipLists("专辑封面保存失败", "专辑封面下载失败");
                }
            }
            else
            {
                using var thumbnail =
                    await HyPlayList.NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999);
                var buffer = new Buffer((uint)thumbnail.Size);
                await thumbnail.ReadAsync(buffer, (uint)thumbnail.Size, InputStreamOptions.None);
                await FileIO.WriteBufferAsync(file, buffer);
                buffer.Length = 0;
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("专辑封面保存失败", ex.Message);
        }
    }

    private void BtnToggleWindowsMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!realclick) return;
        ManualChangeMode = true;
        if (BtnToggleAlbum.IsChecked && BtnToggleLyric.IsChecked)
            WindowMode = ExpandedWindowMode.Both;
        else if (BtnToggleAlbum.IsChecked)
            WindowMode = ExpandedWindowMode.CoverOnly;
        else if (BtnToggleLyric.IsChecked) WindowMode = ExpandedWindowMode.LyricOnly;
        ChangeWindowMode();
    }

    private void BtnToggleFullScreen_Checked(object sender, RoutedEventArgs e)
    {
        if (programClick) return;
        if (BtnToggleFullScreen.IsChecked)
        {
            ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.FullScreen;
            ChangeWindowMode();
        }
        else if (ApplicationView.GetForCurrentView().IsFullScreenMode)
        {
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.Auto;
            ChangeWindowMode();
        }
    }

    private void CopySongName_Click(object sender, RoutedEventArgs e)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(TextBlockSongTitle.Text);
        Clipboard.SetContent(dataPackage);
    }

    private void LyricBoxContainer_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        //LyricBox.ContextFlyout.ShowAt(LyricBox);
    }

    private async void BtnLoadLocalLyric(object sender, RoutedEventArgs e)
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".lrc");
        // register provider - by default encoding is not supported 
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var lrcFile = await fop.PickSingleFileAsync();
        if (lrcFile is null) return;
        var lrcText = await FileIO.ReadTextAsync(lrcFile);
        HyPlayList.Lyrics = Utils.ConvertPureLyric(lrcText, true);
        LoadLyricsBox();
    }

    private void ImageAlbum_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (WindowMode == ExpandedWindowMode.CoverOnly)
        {
            WindowMode = ExpandedWindowMode.LyricOnly;
            ChangeWindowMode();
        }
    }

    private async void LyricBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (WindowMode == ExpandedWindowMode.LyricOnly)
        {
            await Task.Delay(1000);
            if (!jumpedLyrics)
            {
                WindowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
            }
            else
            {
                jumpedLyrics = false;
            }
        }
    }

    private List<Windows.UI.Color> albumColors = new();
    private SolidColorBrush? _pureIdleBrushCache;
    private Windows.UI.Color? _karaokAccentColorCache;
    private SolidColorBrush? _pureAccentBrushCache;

    private async Task<bool> IsBrightAsync(IRandomAccessStream coverStream)
    {
        using var stream = coverStream.CloneStream();
        if (Common.Setting.lyricColor != 0 && Common.Setting.lyricColor != 3) return Common.Setting.lyricColor == 2;
        if (Common.Setting.expandedPlayerBackgroundType >= 2)
            // 强制颜色
            switch (Common.Setting.expandedPlayerBackgroundType)
            {
                case 2 or 5: //System or Desktop Acrylic
                    return Application.Current.RequestedTheme == ApplicationTheme.Light;
                case 3: // White
                    return true;
                case 4: // Black
                    return false;
            }

        if (HyPlayList.NowPlayingItem.PlayItem == null) return false;

        if (lastSongForBrush == HyPlayList.NowPlayingItem.PlayItem) return ForegroundAccentTextBrush.Color.R == 0;
        try
        {
            Buffer buffer = new Buffer(MIMEHelper.PICTURE_FILE_HEADER_CAPACITY);
            stream.Seek(0);
            await stream.ReadAsync(buffer, MIMEHelper.PICTURE_FILE_HEADER_CAPACITY, InputStreamOptions.None);
            var mime = MIMEHelper.GetPictureCodecFromBuffer(buffer);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(mime, stream);
            var color = await Common.ColorThief.GetColor(decoder, ignoreWhite: false);
            if (Common.Setting.expandedPlayerBackgroundType is 6)
            {
                var palette = await Common.ColorThief.GetPalette(decoder, 12, 10, false);
                albumColors = palette
                    .Select(quantizedColor => Windows.UI.Color.FromArgb(
                        quantizedColor.Color.A, quantizedColor.Color.R, quantizedColor.Color.G, quantizedColor.Color.B))
                    .ToList();
            }

            //var c = GetPixel(bytes, 0, 0, decoder.PixelWidth, decoder.PixelHeight);
            lastSongForBrush = HyPlayList.NowPlayingItem.PlayItem;
            albumMainColor = Windows.UI.Color.FromArgb(color.Color.A, color.Color.R, color.Color.G, color.Color.B);
            if (Common.Setting.expandedPlayerBackgroundType is 1 or 6)
            {
                PageContainer.Background =
                    new SolidColorBrush(albumMainColor.Value);
            }

            return !color.IsDark;
        }
        catch
        {
            return ActualTheme == ElementTheme.Light;
        }
    }

    public static Color GetPixel(byte[] pixels, int x, int y, uint width, uint height)
    {
        var i = x;
        var j = y;
        var k = (i * (int)width + j) * 3;
        var r = pixels[k + 0];
        var g = pixels[k + 1];
        var b = pixels[k + 2];
        return Color.FromArgb(0, r, g, b);
    }

    private void LyricOffsetAdd_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LyricOffset = TimeSpan.FromMilliseconds(--offset * 100);
        // TbOffset.Text = (HyPlayList.LyricOffset > TimeSpan.Zero ? "-" : "") +
        //                 HyPlayList.LyricOffset.ToString("ss\\.ff");
    }

    private void LyricOffsetMin_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LyricOffset = TimeSpan.FromMilliseconds(++offset * 100);
        // TbOffset.Text = (HyPlayList.LyricOffset > TimeSpan.Zero ? "-" : "") +
        //                 HyPlayList.LyricOffset.ToString("ss\\.ff");
    }

    private void LyricOffsetUnset_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LyricOffset = TimeSpan.Zero;
        offset = 0;
        // TbOffset.Text = (HyPlayList.LyricOffset < TimeSpan.Zero ? "-" : "") +
        //                 HyPlayList.LyricOffset.ToString("ss\\.ff");
    }

    private void BtnSpeedMinusClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.Player.PlaybackSession.PlaybackRate <= 0.2) return;
        HyPlayList.Player.PlaybackSession.PlaybackRate -= 0.1;
        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void BtnSpeedPlusClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.Player.PlaybackSession.PlaybackRate += 0.1;
        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void TbNowSpeed_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        HyPlayList.Player.PlaybackSession.PlaybackRate = 1.0;
        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void BtnCopyLyricClicked(object sender, RoutedEventArgs e)
    {
        _ = new LyricShareDialog { Lyrics = HyPlayList.Lyrics }.ShowAsync();
    }

    private async void BtnToggleTinyModeClick(object sender, RoutedEventArgs e)
    {
        if (expandedPlayerWindow is null) //判断窗口状态
        {
            expandedPlayerWindow = await AppWindow.TryCreateAsync();
            expandedPlayerWindow.Closed += ExpandedPlayerClosed;
        }

        if (BtnToggleTinyMode.IsChecked)
        {
            Frame expandedPlayerWindowContentFrame = new Frame();
            expandedPlayerWindowContentFrame.Navigate(typeof(CompactPlayerPage), expandedPlayerWindow);
            ElementCompositionPreview.SetAppWindowContent(expandedPlayerWindow, expandedPlayerWindowContentFrame);


            expandedPlayerWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            expandedPlayerWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            expandedPlayerWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;

            expandedPlayerWindow.Presenter.RequestPresentation(AppWindowPresentationKind.CompactOverlay);
            await expandedPlayerWindow.TryShowAsync();
            expandedPlayerWindow.Presenter.RequestPresentation(AppWindowPresentationKind.CompactOverlay); //防止进入失败
        }
        else
        {
            await expandedPlayerWindow.CloseAsync();
        }

        //Common.PageMain.ExpandedPlayer.Navigate(typeof(CompactPlayerPage));
    }

    private void ExpandedPlayerClosed(AppWindow sender, AppWindowClosedEventArgs args)
    {
        BtnToggleTinyMode.IsChecked = false;
        expandedPlayerWindow = null;
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABStartPoint = HyPlayList.Player.PlaybackSession.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABEndPoint = HyPlayList.Player.PlaybackSession.Position;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Common.PageMain.IsExpandedPlayerInitialized = true;
        ToggleButtonSound.IsChecked = Common.ShowLyricSound;
        ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
        if (Common.Setting.albumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(Common.Setting.albumBorderLength);
        switch (Common.Setting.expandedPlayerBackgroundType)
        {
            case 0: // Default
            case 1: // According to Album
                break;
            case 2: // According to System
                PageContainer.Background = new SolidColorBrush(Colors.Transparent);
                break;
            case 3: // Force White
                PageContainer.Background = new SolidColorBrush(Colors.WhiteSmoke);
                break;
            case 4: // Force Black
                PageContainer.Background = new SolidColorBrush(Colors.Black);
                break;
        }

        if (!Common.IsInImmersiveMode)
        {
            if (Common.Setting.albumRotate)
                //网易云音乐圆形唱片
                if (HyPlayList.IsPlaying)
                    RotateAnimationSet.StartAsync();
            if (Common.Setting.expandAlbumBreath)
            {
                ImageAlbumAni.Begin();
            }
        }


        LoadLyricsBox();
    }

    private async void ImageAlbum_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || !Common.Setting.enableTouchGestureAction) return;
        double manipulationDeltaRotateValue = new double();
        switch (Common.Setting.gestureMode)
        {
            case 3:
                if (!Common.Setting.albumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                HyPlayList.Seek(HyPlayList.Player.PlaybackSession.Position.Add(
                    TimeSpan.FromMilliseconds((int)manipulationDeltaRotateValue) * 100));
                break;
            case 2:
                if (!Common.Setting.albumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                return;
            case 1:
                ImagePositionOffset.Y = e.Cumulative.Translation.Y / 10;
                ImagePositionOffset.X = e.Cumulative.Translation.X / 10;
                break;
            case 0 when Math.Abs(e.Cumulative.Translation.Y) > Math.Abs(e.Cumulative.Translation.X):
            {
                // 竖直方向滑动
                if (e.Cumulative.Translation.Y >= 0)
                    Common.PageMain.ExpandedPlayerPositionOffset.Y = e.Cumulative.Translation.Y;
                else
                {
                    ImagePositionOffset.Y = e.Cumulative.Translation.Y / 10;
                }

                if (e.Cumulative.Translation.Y > 200)
                {
                    e.Complete();
                    await Common.BarPlayBar.CollapseExpandedPlayer();
                }

                break;
            }
            case 0:
            {
                ImagePositionOffset.X = e.Cumulative.Translation.X / 10;
                if (e.Cumulative.Translation.X > 400 || e.Cumulative.Translation.X < -400)
                {
                    e.Complete();
                }

                break;
            }
        }
    }

    private async void ImageAlbum_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        Common.PageMain.ImageResetPositionAni.Begin();
        if (Common.Setting.gestureMode == 0)
        {
            if (Math.Abs(e.Cumulative.Translation.Y) < Math.Abs(e.Cumulative.Translation.X))
            {
                // 切换上下曲
                if (e.Cumulative.Translation.X > 150)
                {
                    var ani1 = ImagePositionOffset.CreateDoubleAnimation("X", 1000, 0, null,
                        TimeSpan.FromMilliseconds(100));
                    var ani2 = ImagePositionOffset.CreateDoubleAnimation("X", 0, -ImageAlbum.ActualWidth - 50, null,
                        TimeSpan.FromMilliseconds(100));
                    var sb1 = new Storyboard();
                    var sb2 = new Storyboard();
                    sb1.Children.Add(ani1);
                    sb2.Children.Add(ani2);
                    await sb1.BeginAsync();
                    sb2.Begin();
                    HyPlayList.SongMovePrevious();
                    return;
                }
                else if (e.Cumulative.Translation.X < -150)
                {
                    var ani1 = ImagePositionOffset.CreateDoubleAnimation("X", -1000, 0, null,
                        TimeSpan.FromMilliseconds(100));
                    var ani2 = ImagePositionOffset.CreateDoubleAnimation("X", 0, ImageAlbum.ActualWidth + 50, null,
                        TimeSpan.FromMilliseconds(100));
                    var sb1 = new Storyboard();
                    var sb2 = new Storyboard();
                    sb1.Children.Add(ani1);
                    sb2.Children.Add(ani2);
                    await sb1.BeginAsync();
                    sb2.Begin();
                    HyPlayList.SongMoveNext();
                    return;
                }
            }
        }

        ImageResetPositionAni.Begin();
    }

    public async Task RefreshAlbumCover(int hashCode, IRandomAccessStream coverStream)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
        {
            using var stream = coverStream.CloneStream();
            if (!Common.Setting.noImage)
            {
                try
                {
                    _lyricColorLoaded = false;
                    if (hashCode != HyPlayList.NowPlayingHashCode) return;
                    var isBright = await IsBrightAsync(stream);
                    await ImageAlbumSource.SetSourceAsync(stream);
                    if (Common.Setting.expandedPlayerBackgroundType == 0 && Background?.GetType() != typeof(ImageBrush))
                    {
                        var brush = new ImageBrush
                            { Stretch = Stretch.UniformToFill };
                        Background = brush;
                        brush.ImageSource = (ImageSource)ImageAlbum.Source;
                    }

                    if (hashCode != HyPlayList.NowPlayingHashCode) return;
                    if (Common.Setting.lyricColor != 3 || albumMainColor == null)
                    {
                        if (Common.Setting.expandedPlayerBackgroundType == 0)
                        {
                            if (albumMainColor != null)
                            {
                                var coverColor = albumMainColor.Value;
                                ImmersiveCover.Color = coverColor;
                            }
                        }

                        if (isBright)
                        {
                            ForegroundAccentTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                            ForegroundIdleTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(114, 0, 0, 0));
                            //ImmersiveCover.Color = Windows.UI.Color.FromArgb(255, 210,210, 210);
                        }
                        else
                        {
                            ForegroundAccentTextBrush =
                                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                            ForegroundIdleTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(66, 255, 255, 255));
                            //ImmersiveCover.Color = Windows.UI.Color.FromArgb(255, 35, 35, 35);
                        }
                    }
                    else
                    {
                        ForegroundAccentTextBrush = new SolidColorBrush(albumMainColor.Value);
                        var idleColor = albumMainColor.Value;
                        idleColor.A -= 10;
                        idleColor.R -= 10;
                        idleColor.G -= 10;
                        idleColor.B -= 10;
                        ForegroundIdleTextBrush = new SolidColorBrush(idleColor);
                    }


                    TextBlockSongTitle.Foreground = ForegroundAccentTextBrush;
                    TextBlockSingerNameTip.Foreground = ForegroundIdleTextBrush;
                    TextBlockAlbumNameTip.Foreground = ForegroundIdleTextBrush;
                    TextBlockSinger.Foreground = ForegroundAccentTextBrush;
                    TextBlockAlbum.Foreground = ForegroundAccentTextBrush;
                    if (Common.Setting.playbarBackgroundElay)
                        Common.BarPlayBar.SetPlayBarIdleBackground(ForegroundIdleTextBrush);
                    //LoadLyricsBox();
                    _lyricColorLoaded = true;
                    RefreshLyricColor();

                    if (Common.Setting.expandedPlayerBackgroundType == 6)
                    {
                        BgRect00.Fill = new SolidColorBrush(albumColors[0]);
                        BgRect01.Fill = new SolidColorBrush(albumColors[1]);
                        BgRect02.Fill = new SolidColorBrush(albumColors[2]);
                        BgRect10.Fill = new SolidColorBrush(albumColors[3]);
                        BgRect11.Fill = new SolidColorBrush(albumColors[4]);
                        BgRect12.Fill = new SolidColorBrush(albumColors[5]);
                        BgRect20.Fill = new SolidColorBrush(albumColors[6]);
                        BgRect21.Fill = new SolidColorBrush(albumColors[7]);
                        BgRect22.Fill = new SolidColorBrush(albumColors[8]);
                    }
                }
                catch
                {
                }
            }
        });
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                lastitem = null;
                _ = Common.Invoke(() =>
                {
                    ImageAlbum.Source = null;
                    Background = null;
                });
                ImageAlbumSource = null;
                LyricList.Clear();
            }

            HyPlayList.OnPause -= HyPlayList_OnPause;
            HyPlayList.OnPlay -= HyPlayList_OnPlay;
            HyPlayList.OnPlayItemChange -= OnSongChange;
            HyPlayList.OnLyricLoaded -= HyPlayList_OnLyricLoaded;
            HyPlayList.OnTimerTicked -= HyPlayList_OnTimerTicked;
            Common.OnEnterForegroundFromBackground -= OnEnteringForeground;
            HyPlayList.OnSongCoverChanged -= RefreshAlbumCover;
            Common.OnPlaybarVisibilityChanged -= OnPlaybarVisibilityChanged;
            if (Window.Current != null)
                Window.Current.SizeChanged -= Current_SizeChanged;
            if (Common.Setting.albumRotate)
                RotateAnimationSet.Stop();
            if (!Common.Setting.expandAlbumBreath) return;
            ImageAlbumAni?.Stop();
            disposedValue = true;
        }
    }

    ~ExpandedPlayer()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Task Show()
    {
        time.Reset();
        MainGrid.Margin = new Thickness(0, 0, 0, 80);
        if (Common.IsInImmersiveMode)
        {
            DefaultRow.Height = new GridLength(1.1, GridUnitType.Star);
        }

        var BtnAni = new DoubleAnimation
        {
            To = 1,
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(BtnAni, MoreBtn);
        Storyboard.SetTargetProperty(BtnAni, "Opacity");
        storyboard.Children.Add(BtnAni);
        storyboard.Begin();
        return Task.CompletedTask;
    }

    public async Task Collapse()
    {
        time.Start();
        await Task.Run(async () =>
        {
            while (time.ElapsedMilliseconds < 3000)
            {
                await Task.Delay(10);
            }

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                MainGrid.Margin = new Thickness(0);
                if (Common.IsInImmersiveMode)
                {
                    DefaultRow.Height = new GridLength(1.35, GridUnitType.Star);
                }

                var BtnAni = new DoubleAnimation
                {
                    To = 0,
                    EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
                    EnableDependentAnimation = true
                };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(BtnAni, MoreBtn);
                Storyboard.SetTargetProperty(BtnAni, "Opacity");
                storyboard.Children.Add(BtnAni);
                storyboard.Begin();
            });
        });
        time.Stop();
    }

    private async Task OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!Common.Setting.AutoHidePlaybar) return;
        if (isActivated)
        {
            await Show();
        }
        else
        {
            await Collapse();
        }
    }

    private void BtnToggleImmersiveMode_OnClicked(object sender, RoutedEventArgs e)
    {
        if (Common.Setting.expandedPlayerBackgroundType == 0)
        {
            if (BtnToggleImmersiveMode.IsChecked)
            {
                ImmersiveModeIn();
            }
            else
            {
                ImmersiveModeExit();
            }
        }
        else
        {
            BtnToggleImmersiveMode.IsChecked = false;
            var dialog = new ContentDialog();
            dialog.Title = "请调整背景样式";
            dialog.Content = "沉浸模式只推荐在展开页背景样式为\"专辑背景模糊\"时才能展现最好效果，否则将无法开启沉浸模式\n请在设置中将背景显示设置更改为\"专辑背景模糊\"后再开启沉浸模式";
            dialog.CloseButtonText = "好";
            dialog.IsPrimaryButtonEnabled = true;
            _ = dialog.ShowAsync();
        }
    }

    private void ImmersiveModeIn()
    {
        // if (Common.Setting.AutoHidePlaybar)
        MainGrid.Margin = new Thickness(0, 0, 0, 80);
        DefaultRow.Height = new GridLength(1.1, GridUnitType.Star);
        // Clear Shadow
        AlbumCoverDropShadow.Opacity = 0;
        //MoreBtn.Margin = new Thickness(0,0,30,130);
        Grid.SetRow(LyricBox, 1);
        if (Common.Setting.albumRotate)
            RotateAnimationSet.Stop();
        if (Common.Setting.expandAlbumBreath)
            ImageAlbumAni.Pause();
        ImmersiveModeInAni.Begin();
        LeftPanel.VerticalAlignment = VerticalAlignment.Bottom;
        Common.IsInImmersiveMode = true;
    }

    private void ImmersiveModeExit()
    {
        //MoreBtn.Margin = new Thickness(0, 0, 30, 50);
        MainGrid.Margin = new Thickness(0, 0, 0, 80);
        DefaultRow.Height = new GridLength(25, GridUnitType.Star);
        if (!Common.Setting.albumRound)
            AlbumCoverDropShadow.Opacity = (double)Common.Setting.expandedCoverShadowDepth / 10;
        Grid.SetRow(LyricBox, 0);
        if (Common.Setting.albumRotate)
            RotateAnimationSet.StartAsync();
        if (Common.Setting.expandAlbumBreath)
            ImageAlbumAni.Begin();
        ImmersiveModeOutAni.Begin();
        LeftPanel.VerticalAlignment = VerticalAlignment.Top;
        Common.IsInImmersiveMode = false;
    }
}

internal enum ExpandedWindowMode
{
    Both,
    CoverOnly,
    LyricOnly
}