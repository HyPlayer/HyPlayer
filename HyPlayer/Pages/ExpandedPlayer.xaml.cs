#region

#nullable enable
using ALRC.Abstraction;
using ALRC.Converters;
using ALRC.Converters.Enhancers;
using CommunityToolkit.WinUI.Animations;
using CommunityToolkit.WinUI.Media;
using HyPlayer.Classes;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Utils;
using Impressionist.Abstractions;
using Impressionist.Implementations;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Input;
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
using HyALRCLyricInfo = HyPlayer.Classes.HyALRCLyricInfo;
using Buffer = Windows.Storage.Streams.Buffer;
using LrcConverter = HyPlayer.Classes.LrcConverter;
using Windows.UI.Xaml.Media.Imaging;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ExpandedPlayer : Page
{
    public static readonly DependencyProperty NowPlaybackSpeedProperty = DependencyProperty.Register(
        "NowPlaybackSpeed", typeof(string), typeof(ExpandedPlayer),
        new PropertyMetadata("x1"));


    public bool jumpedLyrics;
    public double lastChangedLyricWidth;
    private int lastheight;
    private bool _lyricHasBeenLoaded = false;
    private bool _lyricIsReadyToGo = false;
    private bool _lyricIsCleaning = false;
    private bool _positionChangedBySeeking = false;


    public HyPlayItem? lastSong;

    private int lastwidth;

    public ObservableCollection<LyricItemModel> LyricList = new();
    private bool ManualChangeMode;
    private int needRedesign = 1;
    private int nowheight;
    private int nowwidth;
    private int offset;
    private bool programClick;
    private bool realclick;
    private ExpandedWindowMode WindowMode;
    private AppWindow? expandedPlayerWindow;
    public Windows.UI.Color? albumMainColor;
    public System.Diagnostics.Stopwatch time = new System.Diagnostics.Stopwatch();
    private PixelShaderEffect? _shaderEffect;
    private float _randomValue = -1;

    private float _lyricRenderXOffset = 0;
    private float _lyricRenderYOffset = 0;

    private readonly Color DarkSpectrumColor = Color.FromArgb(32, 0, 0, 0);
    private readonly Color LightSpectrumColor = Color.FromArgb(32, 255, 255, 255);

    private LyricRenderView LyricBox = new LyricRenderView();


    public ExpandedPlayer()
    {
        InitializeComponent();
        Common.PageExpandedPlayer = this;
        HyPlayList.OnPause += HyPlayList_OnPause;
        HyPlayList.OnPlay += HyPlayList_OnPlay;
        HyPlayList.OnPlayItemChange += OnSongChange;
        HyPlayList.OnSongCoverChanged += RefreshAlbumCover;
        HyPlayList.OnLyricLoaded += HyPlayList_OnLyricLoaded;
        HyPlayList.OnManualSeek += HyPlayList_OnManualSeek;
        Window.Current.SizeChanged += Current_SizeChanged;
        HyPlayList.OnTimerTicked += HyPlayList_OnTimerTicked;
        Common.OnEnterForegroundFromBackground += OnEnteringForeground;
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
        LyricBox.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        LyricBox.OnBeforeRender += LyricBox_OnBeforeRender;
        LyricBox.OnLyricLineClicked += LyricBoxOnOnRequestSeek;
        LyricBox.Context.LyricWidthRatio = Common.Setting.lyricRenderWidthRatio / 100f;
        LyricBox.Context.LyricPaddingTopRatio = Common.Setting.lyricPaddingTopRatio / 100f;
        LyricBox.Context.CurrentLyricTime = 0;
        LyricBox.Context.Debug = Common.Setting.LyricRendererDebugMode;
        LyricBox.Context.Effects.Blur = Common.Setting.lyricRenderBlur;
        LyricBox.Context.LineRollingEaseCalculator = Common.Setting.LineRollingCalculator switch
        {
            1 => new SinRollingCalculator(),
            2 => new LyricifyRollingCalculator(),
            3 => new SyncRollingCalculator(),
            4 => new CircleEaseRollingCalculator(),
            _ => new ElasticEaseRollingCalculator()
        };
        LyricBox.Context.Effects.ScaleWhenFocusing = Common.Setting.lyricRenderScaleWhenFocusing;
        LyricBox.Context.Effects.FocusHighlighting = Common.Setting.lyricRenderFocusHighlighting;
        LyricBox.Context.Effects.TransliterationScanning = Common.Setting.lyricRenderTransliterationScanning;
        LyricBox.Context.Effects.SimpleLineScanning = Common.Setting.lyricRenderSimpleLineScanning;
        LyricBox.Context.PreferTypography.Font = Common.Setting.lyricFontFamily;
        LyricBox.Context.LineSpacing = Common.Setting.lyricLineSpacing;
    }

    private void HyPlayList_OnManualSeek(TimeSpan position)
    {
        _positionChangedBySeeking = true;
    }

    private void LyricBoxOnOnRequestSeek(RenderingLyricLine line)
    {
        jumpedLyrics = true;
        if (line is ActionLyricLine actionLyricLine)
        {
            var action = actionLyricLine.ActionUri;
            if (action.StartsWith("hyplayer://"))
            {
                var ResourceId = action.Substring(11);
                _ = Common.NavigatePageResource(ResourceId);
                Common.BarPlayBar!.CollapseExpandedPlayer();
            }
            else
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(action));
            }
        }
        else
        {
            HyPlayList.Seek(TimeSpan.FromMilliseconds(line.StartTime));
        }
    }

    private void LyricBox_OnBeforeRender(LyricRenderer.LyricRenderView view)
    {
        view.Context.IsPlaying = HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing;
        if (HyPlayList.Player.PrimaryAudioInputNode == null)
        {
            view.Context.CurrentLyricTime = 0;
            return;
        }
        if (HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds < view.Context.CurrentLyricTime)
        {
            view.Context.CurrentLyricTime = (long)(HyPlayList.Player?.PrimaryAudioInputNode.Position.TotalMilliseconds ?? 0);
            LyricBox.ReflowTime(0);
        }
        else
        {
            view.Context.CurrentLyricTime = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds;
        }
        view.Context.IsSeek = _positionChangedBySeeking;
        _positionChangedBySeeking = false;

    }

    public double showsize { get; set; }
    public double LyricWidth { get; set; }

    public string NowPlaybackSpeed
    {
        get => (string)GetValue(NowPlaybackSpeedProperty);
        set => SetValue(NowPlaybackSpeedProperty, value);
    }

    public void SingleViewModeToggle()
    {
        if (WindowMode == ExpandedWindowMode.Both) return;
        else
        {
            WindowMode = WindowMode == ExpandedWindowMode.LyricOnly ? ExpandedWindowMode.CoverOnly : ExpandedWindowMode.LyricOnly;
            ChangeWindowMode();
        }
    }
    private void HyPlayList_OnPlay()
    {
        _ = Common.Invoke(() =>
        {
            if (Common.Setting.albumRotate)
                //网易云音乐圆形唱片
                RotateAnimationSet.StartAsync();
            if (Common.Setting.expandAlbumBreath)
            {
                ImageAlbumAni.Begin();
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
        if (needRedesign > 0)
        {
            needRedesign--;
            Redesign();
        }
    }


    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        HyPlayList.OnPause -= HyPlayList_OnPause;
        HyPlayList.OnPlay -= HyPlayList_OnPlay;
        HyPlayList.OnPlayItemChange -= OnSongChange;
        HyPlayList.OnLyricLoaded -= HyPlayList_OnLyricLoaded;
        HyPlayList.OnTimerTicked -= HyPlayList_OnTimerTicked;
        HyPlayList.OnManualSeek -= HyPlayList_OnManualSeek;
        Common.OnEnterForegroundFromBackground -= OnEnteringForeground;
        HyPlayList.OnSongCoverChanged -= RefreshAlbumCover;
        Common.OnPlaybarVisibilityChanged -= OnPlaybarVisibilityChanged;
        if (Window.Current != null)
            Window.Current.SizeChanged -= Current_SizeChanged;
        if (Common.Setting.albumRotate)
            RotateAnimationSet.Stop();
        if (Common.Setting.expandAlbumBreath)
        {
            ImageAlbumAni?.Stop();
        }
        _shaderEffect = null;
    }

    private void HyPlayList_OnLyricLoaded()
    {
        LoadLyricsBox();
        needRedesign++;
    }

    private void Current_SizeChanged(object? sender, WindowSizeChangedEventArgs? e)
    {
        nowwidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
        nowheight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
        if (lastwidth != nowwidth)
        {
            //这段不要放出去了
            if (WindowMode == ExpandedWindowMode.Both)
                LyricWidth = nowwidth * 0.5;
            else
                LyricWidth = nowwidth - 15;
            LyricWidth = Math.Max(LyricWidth, 0);
            showsize = Common.Setting.lyricSize <= 0
                ? Math.Max(nowwidth / 40, 40)
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

        if (WindowMode == ExpandedWindowMode.Both)
            LyricWidth = nowwidth * 0.5;
        else
            LyricWidth = nowwidth - 30;
        LyricWidth = Math.Max(LyricWidth, 0);

        switch (WindowMode)
        {
            case ExpandedWindowMode.Both:
                BtnToggleAlbum.IsChecked = true;
                BtnToggleLyric.IsChecked = true;
                RightPanel.Visibility = Visibility.Visible;
                UIAugmentationSys.Visibility = Visibility.Visible;
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
                break;
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
        if (Math.Abs(lastChangedLyricWidth - LyricWidth) > 0.001f && Math.Abs(_lyricRenderXOffset - RightPanel.ActualOffset.X) > 0.001f)
        {
            _lyricRenderXOffset = RightPanel.ActualOffset.X;
            _lyricRenderYOffset = RightPanel.ActualOffset.Y;
            LyricBox.Redesign((float)LyricWidth, nowheight);
            LyricBox.ChangeRenderFontSize((float)showsize,
                (Common.Setting.translationSize > 0) ? Common.Setting.translationSize : (float)showsize / 2,
                (Common.Setting.romajiSize > 0) ? Common.Setting.romajiSize : (float)showsize / 2);
            lastChangedLyricWidth = LyricWidth;
        }

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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Common.IsInBackground = false;
        Common.PageExpandedPlayer = this;
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
            RefreshAlbumCover(HyPlayList.NowPlayingItem);
            ChangeWindowMode();
            needRedesign++;
        }
        catch
        {
        }

        if (Common.Setting.expandedPlayerBackgroundType == 0 && !Common.Setting.expandedUseAcrylic)
            AcrylicCover.Fill = new BackdropBlurBrush { Amount = 50.0 };
        if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.Animated)
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
        if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.DesktopAcrylic)
            PageContainer.Background =
                (Brush)new BooleanToWindowBrushesConverter().Convert(
                    Common.Setting.acrylicBackgroundStatus, null, null,
                    null);

        NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        if (Common.Setting.pureLyricFocusingColor is not null)
        {
            _pureAccentBrushCache ??= Common.BrushManagement.AccentBrush;
        }
        if (Common.Setting.pureLyricIdleColor is not null)
        {
            _pureIdleBrushCache ??= Common.BrushManagement.IdleBrush;
        }
        if (Common.Setting.karaokLyricFocusingColor is not null)
        {
            _karaokAccentColorCache ??= Common.BrushManagement.KaraokAccentBrush;
        }
    }


    private Storyboard bpmAniStoryboard = new Storyboard();
    public void InitializeBPM()
    {
        // TODO: 完成 BPM API
        /*
        bpmAniStoryboard.Stop();    
        if (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Netease ||
            string.IsNullOrEmpty(HyPlayList.NowPlayingItem.PlayItem.Id))
            return;
        var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.SongWikiSummaryApi, new SongWikiSummaryRequest() { SongId = HyPlayList.NowPlayingItem.PlayItem.Id });
        if (json.IsError) return;
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

                }
            }
        }
        */
    }

    public void LoadLyricsBox()
    {
        _ = Common.Invoke(() =>
        {
            _lyricIsReadyToGo = true;
            if (_lyricIsCleaning) return;
            if (HyPlayList.HyLyricInfo.PureLyricInfo is not HyALRCLyricInfo alrcLyricInfo)
            {
                LyricBox.SetLyricLines(LrcConverter.Convert(ConvertToALRC(HyPlayList.HyLyricInfo.Lyrics), HyPlayList.HyLyricInfo.LyricMetadata, HyPlayList.HyLyricInfo.SongMetadata));
            }
            else
            {
                LyricBox.SetLyricLines(LrcConverter.Convert(alrcLyricInfo.ALRC, alrcLyricInfo.LyricMetadata, alrcLyricInfo.SongMetadata));
            }
            LyricBox.ChangeAlignment(Common.Setting.lyricAlignment switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left
            });
            LyricBox.ReflowTime(0);
            if (HyPlayList.NowPlayingItem == null) return;
            LyricBox.Redesign((float)LyricWidth, nowheight);
            LyricBox.ChangeRenderColor(Common.BrushManagement.IdleBrush.Color, Common.BrushManagement.AccentBrush.Color);
            Redesign();
        });
    }

    public static ALRCFile ConvertToALRC(List<SongLyric> lyric)
    {
        var lines = new List<ALRCLine>();
        var alrc = new ALRCFile
        {
            Schema = "https://github.com/kengwang/ALRC/blob/main/schemas/v1.json",
            LyricInfo = null,
            SongInfo = null,
            Header = null,
            Lines = lines
        };
        var lastLine = new ALRCLine();
        foreach (var songLyric in lyric)
        {
            var line = new ALRCLine
            {
                Start = (long)songLyric.LyricLine.StartTime.TotalMilliseconds,
                LineStyle = null,
                RawText = songLyric.LyricLine.CurrentLyric,
                Transliteration = songLyric.Romaji?.Trim(),
                Translation = songLyric.Translation?.Trim()
            };
            lastLine.End = line.Start;
            lastLine = line;
            if (songLyric.LyricLine is KaraokeLyricsLine lrcLyricsLine)
            {
                line.Words = lrcLyricsLine.WordInfos.Select(s => new ALRCWord
                {
                    Start = (long)s.StartTime.TotalMilliseconds,
                    End = (long)(s.StartTime + s.Duration).TotalMilliseconds,
                    Word = s.CurrentWords,
                    Transliteration = string.IsNullOrWhiteSpace(s.Transliteration) ? null : s.Transliteration
                }).ToList();
            }
            lines.Add(line);
        }

        if (lines.LastOrDefault() is { End: null or <= 0 } last) last.End = (long)(HyPlayList.Player.PrimaryAudioInputNode?.Duration.TotalMilliseconds ?? 0);

        return alrc;
    }






    public void OnEnteringForeground()
    {
        OnSongChange(HyPlayList.NowPlayingItem);
        RefreshAlbumCover(HyPlayList.NowPlayingItem);
        if (!_lyricHasBeenLoaded) HyPlayList_OnLyricLoaded();
    }

    public void OnSongChange(HyPlayItem mpi)
    {
        var lyricIsReady = lastSong == HyPlayList.NowPlayingItem;
        _lyricIsReadyToGo = lyricIsReady;
        _lyricHasBeenLoaded = lyricIsReady;
        _ = Common.Invoke(() =>
        {
            var artistText = mpi?.PlayItem?.ArtistString;
            SingerTextBlock.Text = artistText;
            TextBlockSongTitle.Text = mpi?.PlayItem?.Name;
            AlbumTextBlock.Text = mpi?.PlayItem?.AlbumString;
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

                    LyricBox.Redesign((float)LyricWidth, nowheight);
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
            NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        });
    }

    public void RefreshUIColor()
    {
        LyricBox.ChangeRenderColor(Common.BrushManagement.IdleBrush.Color, Common.BrushManagement.AccentBrush.Color);
    }

    public void StartExpandAnimation()
    {
        ImageAlbum.Visibility = Visibility.Visible;
        SingerHyperlinkBtn.Visibility = Visibility.Visible;
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
                Common.BarPlayBar!.GridSongInfoContainer.Visibility == Visibility.Visible)
            {
                if (TextBlockSongTitle.ActualSize.X != 0 && TextBlockSongTitle.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
                if (ImageAlbum.ActualSize.X != 0 && ImageAlbum.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);
                if (SingerHyperlinkBtn.ActualSize.X != 0 && SingerHyperlinkBtn.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", SingerHyperlinkBtn);
                if (AlbumHyperlinkBtn.ActualSize.X != 0 && AlbumHyperlinkBtn.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongAlbum", AlbumHyperlinkBtn);
            }
        }
        catch
        {
            //ignore
        }
    }

    private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerWheelChanged(sender, e);
    }

    private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.IsPlaying)
            HyPlayList.Player.PauseAll();
        else
            HyPlayList.Player.PlayAll();
    }

    private void ToggleButtonTranslation_OnClick(object sender, RoutedEventArgs e)
    {
        Common.ShowLyricTrans = ToggleButtonTranslation.IsChecked;
        if (LyricBox != null)
        {
            LyricBox.EnableTranslation = Common.ShowLyricTrans;
        }

    }

    private void ToggleButtonSound_OnClick(object sender, RoutedEventArgs e)
    {
        Common.ShowLyricSound = ToggleButtonSound.IsChecked;
        if (LyricBox != null)
        {
            LyricBox.EnableTransliteration = Common.ShowLyricSound;
        }

    }

    private void AlbumHyperlinkBtn_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
                if (HyPlayList.NowPlayingItem.PlayItem.Album.Id != "0")
                    Common.NavigatePage(typeof(AlbumPage),
                        HyPlayList.NowPlayingItem.PlayItem.Album.Id);

            if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                Common.NavigatePage(typeof(RadioPage), HyPlayList.NowPlayingItem.PlayItem.Album.Id);

            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            Common.BarPlayBar!.CollapseExpandedPlayer();
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
                    HyPlayList.NowPlayingItem.PlayItem.Artist[0].Id);
            }

            if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].Id);

            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            Common.BarPlayBar!.CollapseExpandedPlayer();
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
            filepicker.SuggestedFileName = HyPlayList.NowPlayingItem.PlayItem.Name + "-Cover.jpg";
            filepicker.FileTypeChoices.Add("图片文件", new List<string> { ".png", ".jpg" });
            var file = await filepicker.PickSaveFileAsync();
            if (file == null) return;
            if (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Local ||
                HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.LocalProgressive)
            {
                using var coverResult =
                    await Common.HttpClient!.GetAsync(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.Cover));
                if (coverResult.IsSuccessStatusCode)
                {
                    var Cover = (await coverResult.Content.ReadAsByteArrayAsync()).AsBuffer();
                    await FileIO.WriteBufferAsync(file, Cover);
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
            ChangeWindowMode();
        }
        else if (ApplicationView.GetForCurrentView().IsFullScreenMode)
        {
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
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
        FileOpenPicker picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".qrc");
        picker.FileTypeFilter.Add(".lrc");
        picker.FileTypeFilter.Add(".yrc");
        picker.FileTypeFilter.Add(".alrc");
        picker.FileTypeFilter.Add(".ttml");
        picker.FileTypeFilter.Add(".lys");
        var sf = await picker.PickSingleFileAsync();
        if (sf != null)
        {
            var qrc = await FileIO.ReadTextAsync(sf);
            ILyricConverter<string> converter = sf.FileType switch
            {
                ".qrc" => new QQLyricConverter(),
                ".yrc" => new NeteaseYrcConverter(),
                ".lrc" => new ALRC.Converters.LrcConverter(),
                ".alrc" => new ALRCConverter(),
                ".ttml" => new AppleSyllableConverter(),
                ".lys" => new LyricifySyllableConverter(),
                _ => throw new ArgumentOutOfRangeException()
            };

            var lrcConverter = new ALRC.Converters.LrcConverter();
            var lrcTranslationConverter = new LrcTranslationEnhancer();
            var alrc = converter.Convert(qrc);
            var lrc = lrcConverter.ConvertBack(alrc);
            var trLrc = lrcTranslationConverter.Extract(alrc);

            HyALRCLyricInfo ttmlLyric = new HyALRCLyricInfo()
            {
                PureLyrics = lrc,
                TrLyrics = trLrc,
                ALRC = alrc,
                LyricMetadata =
                [
                    new LyricInfoMetadata
                    {
                        Key = "lyric_source",
                        Value = "本地歌词",
                        DisplayName = "歌词来源",
                        ActionUri = sf.Path
                    }
                ],
                SongMetadata = []
            };

            HyPlayList.HyLyricInfo = new HyLyricInfo();
            HyPlayList.HyLyricInfo.LyricMetadata = ttmlLyric.LyricMetadata;
            HyPlayList.HyLyricInfo.PureLyricInfo = ttmlLyric;
            HyPlayList.HyLyricInfo.SongMetadata = ttmlLyric.SongMetadata;
            HyPlayList.HyLyricInfo.Lyrics = Utils.ConvertPureLyric(ttmlLyric.PureLyrics, true);
            Utils.ConvertTranslation(ttmlLyric.TrLyrics, HyPlayList.HyLyricInfo.Lyrics);
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                _ = SimpleCacher.GetOrCreateCacheAsync(CacheType.HyLyricInfo, HyPlayList.NowPlayingItem.PlayItem.Id,
                    () => Task.FromResult(HyPlayList.HyLyricInfo)!, forceRefresh: true);
            }


            var lrcs = LrcConverter.Convert(alrc);
            LyricBox.SetLyricLines(lrcs);
        }
    }

    private async void LyricBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (WindowMode == ExpandedWindowMode.LyricOnly)
        {
            UISettings _uiSettings = new UISettings();
            await Task.Delay((int)(_uiSettings.DoubleClickTime + 55));
            if (!LyricBox.HasJumpedLyrics)
            {
                WindowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
            }
        }
    }

    private List<Vector3> albumColorVectors = new();
    private List<Windows.UI.Color> albumColors = new();
    private SolidColorBrush? _pureIdleBrushCache;
    private Windows.UI.Color? _karaokAccentColorCache;
    private SolidColorBrush? _pureAccentBrushCache;

    private async Task<bool> IsBrightAsync(IRandomAccessStream stream)
    {
        lastSong = HyPlayList.NowPlayingItem;
        var finalResult = false; //在不手动指定背景类型为2至5时需要执行颜色采样
        var resultGenerated = false; //标志返回颜色已经生成
        if (Common.Setting.lyricColor != 0 && Common.Setting.lyricColor != 3)
        {
            finalResult = Common.Setting.lyricColor == 2;
            resultGenerated = true;
        }
        if (HyPlayList.NowPlayingItem.PlayItem == null) return false;
        if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.DesktopAcrylic)
        {
            return ActualTheme == ElementTheme.Light;
        }
        try
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            var colors = await ImageDecoder.GetPixelColor(decoder);
            ThemeColorResult themeColor;
            PaletteResult palette;
            if (Common.Setting.expandedPlayerBackgroundType != BackgroundType.Animated && Common.Setting.expandedPlayerBackgroundType != BackgroundType.Isolation)
            {
                if (Common.Setting.ColorGeneratorType is 0)
                {
                    themeColor = await PaletteGenerators.KMeansPaletteGenerator.CreateThemeColor(colors, Common.Setting.ImpressionistIgnoreWhite, Common.Setting.ImpressionistLABSpace);
                }
                else
                {
                    themeColor = await PaletteGenerators.OctTreePaletteGenerator.CreateThemeColor(colors, Common.Setting.ImpressionistIgnoreWhite);
                }
                albumMainColor = Windows.UI.Color.FromArgb(255, (byte)themeColor.Color.X, (byte)themeColor.Color.Y, (byte)themeColor.Color.Z);
            }
            else
            {
                if (Common.Setting.ColorGeneratorType is 0)
                {
                    palette = await PaletteGenerators.KMeansPaletteGenerator.CreatePalette(colors,
                                                                                           Common.Setting.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                                                                                           Common.Setting.ImpressionistIgnoreWhite,
                                                                                           Common.Setting.ImpressionistLABSpace,
                                                                                           Common.Setting.ImpressionistUseKMeansPP);
                }
                else
                {
                    palette = await PaletteGenerators.OctTreePaletteGenerator.CreatePalette(colors,
                                                                                           Common.Setting.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                                                                                           Common.Setting.ImpressionistIgnoreWhite);
                }
                themeColor = palette.ThemeColor;
                albumColors = palette.Palette.Select(quantizedColor => Windows.UI.Color.FromArgb(255, (byte)quantizedColor.X, (byte)quantizedColor.Y, (byte)quantizedColor.Z))
                    .ToList();
                albumMainColor = Windows.UI.Color.FromArgb(255, (byte)themeColor.Color.X, (byte)themeColor.Color.Y, (byte)themeColor.Color.Z);
                albumColorVectors = palette.Palette.Select(t => t / 255).ToList();
            }
            if (Common.Setting.expandedPlayerBackgroundType is BackgroundType.CoverTheme)
            {
                PageContainer.Background =
                    new SolidColorBrush(albumMainColor!.Value);
            }
            if (!resultGenerated)
            {
                finalResult = !themeColor.ColorIsDark;
                resultGenerated = true;
            }
        }
        catch
        {
            if (!resultGenerated)
            {
                finalResult = ActualTheme == ElementTheme.Light;
                resultGenerated = true;
            }
        }
        return finalResult;
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
        if (HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource == null) return;
        var currentSpeed = HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        var newSpeed = Math.Max(0.5, currentSpeed - 0.1);
        HyPlayList.Player.SetPlaybackSourceSpeed(newSpeed, HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
    }

    private void BtnSpeedPlusClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource == null) return;
        var currentSpeed = HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        var newSpeed = Math.Min(2.0, currentSpeed + 0.1);
        HyPlayList.Player.SetPlaybackSourceSpeed(newSpeed, HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
    }

    private void TbNowSpeed_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        HyPlayList.Player.SetPlaybackSourceSpeed(1, HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
    }

    private void BtnCopyLyricClicked(object sender, RoutedEventArgs e)
    {
        _ = new LyricShareDialog { Lyrics = HyPlayList.HyLyricInfo.Lyrics }.ShowAsync();
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
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABStartPoint = HyPlayList.Player.PrimaryAudioInputNode.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABEndPoint = HyPlayList.Player.PrimaryAudioInputNode.Position;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Common.PageMain!.IsExpandedPlayerInitialized = true;
        ToggleButtonSound.IsChecked = Common.ShowLyricSound;
        ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
        if (Common.Setting.albumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(Common.Setting.albumBorderLength);
        switch (Common.Setting.expandedPlayerBackgroundType)
        {
            case BackgroundType.CoverBlur: // Default
            case BackgroundType.CoverTheme: // According to Album
                break;
            case BackgroundType.Animated:
                BlackCover.Opacity = 1;
                break;
            case BackgroundType.DesktopAcrylic:
            case BackgroundType.Isolation:
                BlackCover.Visibility = Visibility.Collapsed;
                AcrylicCover.Visibility = Visibility.Collapsed;
                break;
        }

        if (Common.Setting.albumRotate)
            //网易云音乐圆形唱片
            if (HyPlayList.IsPlaying)
                _ = RotateAnimationSet.StartAsync();
        if (Common.Setting.expandAlbumBreath)
        {
            ImageAlbumAni.Begin();
        }


        if (bpmAniStoryboard.Children.Count > 0)
        {
            bpmAniStoryboard.Resume();
        }

        if (luminousColorsRotateStoryBoard.Children.Count > 0)
        {
            luminousColorsRotateStoryBoard.Resume();
        }

        LoadLyricsBox();
    }

    private void ImageAlbum_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || !Common.Setting.enableTouchGestureAction) return;
        double manipulationDeltaRotateValue;
        switch (Common.Setting.gestureMode)
        {
            case 3:
                if (!Common.Setting.albumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                HyPlayList.Seek(HyPlayList.Player.PrimaryAudioInputNode.Position.Add(
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
                        Common.PageMain!.ExpandedPlayerPositionOffset.Y = e.Cumulative.Translation.Y;
                    else
                    {
                        ImagePositionOffset.Y = e.Cumulative.Translation.Y / 10;
                    }

                    if (e.Cumulative.Translation.Y > 200)
                    {
                        e.Complete();
                        Common.BarPlayBar!.CollapseExpandedPlayer();
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
        Common.PageMain!.ImageResetPositionAni.Begin();
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

    public static Windows.UI.Color AdjustBrightness(Windows.UI.Color color, float percentage)
    {
        int adjustment = (int)(255 * percentage);
        int r = Math.Max(0, Math.Min(255, color.R + adjustment));
        int g = Math.Max(0, Math.Min(255, color.G + adjustment));
        int b = Math.Max(0, Math.Min(255, color.B + adjustment));
        return Windows.UI.Color.FromArgb(color.A, (byte)r, (byte)g, (byte)b);
    }


    public async void RefreshAlbumCover(HyPlayItem playItem)
    {
        if (HyPlayList.CoverStream == null) return;
        _ = Common.Invoke(async () =>
        {
            if (!Common.Setting.noImage)
            {
                try
                {
                    if (playItem != HyPlayList.NowPlayingItem) return;
                    using var stream = HyPlayList.CoverStream.CloneStream();
                    var isBright = await IsBrightAsync(stream);
                    Common.BrushManagement.IsBright = isBright;
                    stream.Seek(0);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    ImageAlbum.Source = bitmap;
                    if (Common.Setting.expandedPlayerBackgroundType == 0 && Background is not ImageBrush)
                    {
                        var brush = new ImageBrush
                        { Stretch = Stretch.UniformToFill };
                        Background = brush;
                    }
                    if(Background is ImageBrush imageBrush)
                    {
                        imageBrush.ImageSource = bitmap;
                    }

                    if (playItem != HyPlayList.NowPlayingItem) return;
                    if (albumMainColor != null)
                    {
                        var coverColor = albumMainColor.Value;
                    }
                    if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.Animated && isBright)
                        BlackCover.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 255, 255));
                    else if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.Animated && !isBright)
                        BlackCover.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 0, 0));
                    if (Common.Setting.lyricColor != 3 || albumMainColor == null)
                    {
                        if (isBright)
                        {
                            Common.BrushManagement.AccentBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                            Common.BrushManagement.IdleBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(114, 0, 0, 0));
                        }
                        else
                        {
                            Common.BrushManagement.AccentBrush =
                                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                            Common.BrushManagement.IdleBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(66, 255, 255, 255));
                        }
                    }
                    else
                    {
                        if (Common.Setting.expandedPlayerBackgroundType != 0)
                        {
                            if (isBright)
                            {
                                var AccentColor = AdjustBrightness((Windows.UI.Color)albumMainColor, -0.3f);
                                Common.BrushManagement.AccentBrush = new SolidColorBrush(AccentColor);
                                var idleColor = AccentColor;
                                idleColor.A = 150;
                                Common.BrushManagement.IdleBrush = new SolidColorBrush(idleColor);

                            }
                            else
                            {
                                var AccentColor = AdjustBrightness((Windows.UI.Color)albumMainColor, 0.3f);
                                Common.BrushManagement.AccentBrush = new SolidColorBrush(AccentColor);
                                var idleColor = AdjustBrightness((Windows.UI.Color)AccentColor, -0.15f);
                                idleColor.A = 150;
                                Common.BrushManagement.IdleBrush = new SolidColorBrush(idleColor);
                            }
                        }
                        else
                        {
                            var AccentColor = AdjustBrightness((Windows.UI.Color)albumMainColor, -0.3f);
                            Common.BrushManagement.AccentBrush = new SolidColorBrush(AccentColor);
                            var idleColor = AccentColor;
                            idleColor.A = 150;
                            Common.BrushManagement.IdleBrush = new SolidColorBrush(idleColor);
                        }
                    }



                    if (Common.Setting.playbarBackgroundElay)
                        Common.BarPlayBar!.SetPlayBarIdleBackground(Common.BrushManagement.IdleBrush);
                    //LoadLyricsBox();
                    RefreshUIColor();
                    if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.Animated)
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
                    if (Common.Setting.expandedPlayerBackgroundType == BackgroundType.Isolation)
                    {
                        if (_shaderEffect != null)
                        {
                            _shaderEffect.Properties["color1"] = albumColorVectors[0];
                            _shaderEffect.Properties["color2"] = albumColorVectors[1];
                            _shaderEffect.Properties["color3"] = albumColorVectors[2];
                            _shaderEffect.Properties["color4"] = albumColorVectors[3];
                            var random = new Random();
                            _shaderEffect.Properties["RandomValue1"] = (float)random.Next(-50, +50);
                            _shaderEffect.Properties["RandomValue2"] = (float)random.Next(-50, +50);
                            _shaderEffect.Properties["RandomValue3"] = (float)random.Next(-50, +50);
                        }
                    }
                }
                catch
                {
                }
            }
        });
    }


    private void LuminousBackground_OnUnloaded(object sender, RoutedEventArgs e)
    {
        LuminousBackground.RemoveFromVisualTree();
        LuminousBackground = null;
    }
    public Task Show()
    {
        time.Reset();
        MainGrid.Margin = new Thickness(0, 0, 0, 80);
        //if (Common.IsInImmersiveMode)
        //{
        //    DefaultRow.Height = new GridLength(1.1, GridUnitType.Star);
        //}

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

    private void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!Common.Setting.AutoHidePlaybar) return;
        if (isActivated)
        {
            Show();
        }
        else
        {
            _ = Collapse();
        }
    }
    public static double Map(double value, double fromSource, double toSource, double fromTarget, double toTarget)
    {
        return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
    }

    private void LuminousBackground_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_shaderEffect != null)
        {
            _shaderEffect.Properties["Width"] = (float)LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualWidth, Microsoft.Graphics.Canvas.CanvasDpiRounding.Round);
            _shaderEffect.Properties["Height"] = (float)LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualHeight, Microsoft.Graphics.Canvas.CanvasDpiRounding.Round);
        }
    }

    private async void LuminousBackground_CreateResources(Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        if (_shaderEffect == null && Common.Setting.expandedPlayerBackgroundType == BackgroundType.Isolation)
        {
            if (Common.PixelShaderShareEffect == null)
            {
                StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Shaders/BackgroundShader.bin"));
                IBuffer buffer = await FileIO.ReadBufferAsync(file);
                var bytes = buffer.ToArray();
                Common.PixelShaderShareEffect = new PixelShaderEffect(bytes);
            }
            _shaderEffect = Common.PixelShaderShareEffect;
            _randomValue = new Random().Next(100);
            if (albumColorVectors.Count != 0)
            {
                _shaderEffect.Properties["color1"] = albumColorVectors[0];
                _shaderEffect.Properties["color2"] = albumColorVectors[1];
                _shaderEffect.Properties["color3"] = albumColorVectors[2];
                _shaderEffect.Properties["color4"] = albumColorVectors[3];
            }
        }
        LuminousBackground.DpiScale = Common.Setting.IsolationScale;
        _shaderEffect?.Properties["Width"] = (float)LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualWidth, Microsoft.Graphics.Canvas.CanvasDpiRounding.Round);
        _shaderEffect?.Properties["Height"] = (float)LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualHeight, Microsoft.Graphics.Canvas.CanvasDpiRounding.Round);
        _shaderEffect?.Properties["EnableLightWave"] = Common.Setting.IsolationLightWave;
        var random = new Random();
        _shaderEffect?.Properties["RandomValue1"] = (float)random.Next(-50, +50);
        _shaderEffect?.Properties["RandomValue2"] = (float)random.Next(-50, +50);
        _shaderEffect?.Properties["RandomValue3"] = (float)random.Next(-50, +50);
        if (!Common.Setting.IsolationFullThrottle)
        {
            LuminousBackground.IsFixedTimeStep = true;
            LuminousBackground.TargetElapsedTime = TimeSpan.FromMilliseconds(16.6 * (60d / Common.Setting.IsolationFPS));
        }
    }

    private void LuminousBackground_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
    {
        if (HyPlayList.IsPlaying && Common.Setting.expandedPlayerBackgroundType == BackgroundType.Isolation)
        {
            var progress = (float)args.Timing.TotalTime.TotalSeconds + _randomValue;
            _shaderEffect?.Properties["iTime"] = progress;
        }
    }

    private void LuminousBackground_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
    {
        using var session = args.DrawingSession;
        if (_shaderEffect != null && Common.Setting.expandedPlayerBackgroundType == BackgroundType.Isolation)
        {
            session.DrawImage(_shaderEffect);
        }
        if (Common.Setting.EnableFFT)
        {
            DrawAudioFFTGraph(sender, session);
        }
        using var lyricCommand = new CanvasCommandList(session);
        using var lyricSession = lyricCommand.CreateDrawingSession();
        LyricBox.Draw(lyricSession, args.Timing); ;
        session.DrawImage(lyricCommand, _lyricRenderXOffset, _lyricRenderYOffset);
    }

    
    private void DrawAudioFFTGraph(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, CanvasDrawingSession session)
    {
        var fftTrans = HyPlayList.Player.FFTProcessor;
        float width = (float)sender.Size.Width;
        float height = (float)sender.Size.Height / 2;
        float remainHeight = (float)sender.Size.Height - height;
        float barWidth = width / FFTProcessor.DisplayBandCount;
        float scaleFactor = height / 80.0f; // 根据分贝值调整高度缩放
        for (int i = 0; i < FFTProcessor.DisplayBandCount; i++)
        {
            float barHeight = Math.Clamp(fftTrans.DisplayData[i] * scaleFactor, 0, height - 1);
            // 使用渐变色会更好看，这里为了性能演示用纯色
            session.FillRectangle(
                i * barWidth,
                remainHeight + height - barHeight,
                barWidth, // -1 留出间隔
                barHeight,
                Common.BrushManagement.IsBright ? DarkSpectrumColor : LightSpectrumColor);
        }
    }

    private void LeftPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (WindowMode == ExpandedWindowMode.Both) return;
        else
        {
            WindowMode = WindowMode == ExpandedWindowMode.LyricOnly ? ExpandedWindowMode.CoverOnly : ExpandedWindowMode.LyricOnly;
            ChangeWindowMode();
        }
    }

    private void LyricView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        LyricBox.OnDoubleTapped(sender, e);
    }

    private void LyricView_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerExited(sender, e);
    }

    private void LyricView_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerMoved(sender, e);
    }

    private void LyricView_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerPressed(sender, e);
    }

    private void LyricView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_PointerReleased(sender, e);
    }
}

internal enum ExpandedWindowMode
{
    Both,
    CoverOnly,
    LyricOnly
}
