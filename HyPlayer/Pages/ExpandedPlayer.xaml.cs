#region

#nullable enable
using ALRC.Abstraction;
using ALRC.Converters;
using ALRC.Converters.Enhancers;
using CommunityToolkit.Mvvm.DependencyInjection;
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
using HyPlayer.ViewModels;
using Impressionist.Abstractions;
using Impressionist.Implementations;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Buffer = Windows.Storage.Streams.Buffer;
using HyALRCLyricInfo = HyPlayer.Classes.HyALRCLyricInfo;
using LrcConverter = HyPlayer.Classes.LrcConverter;

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
    private bool _lyricHasBeenLoaded = false;
    private bool _lyricIsCleaning = false;
    private bool _positionChangedBySeeking = false;
    private int _lastHeight;
    private int _lastWidth;
    public HyPlayItem? _lastSong;
    private bool _isManualChangeMode;
    private int _needRedesign = 1;
    private int _nowHeight;
    private int _nowWidth;
    private bool _isProgramClick;
    private bool _isRealClick;
    private ExpandedWindowMode _windowMode;
    private AppWindow? expandedPlayerWindow;
    public Color? albumMainColor;
    public Stopwatch _stopwatch = new();
    private PixelShaderEffect? _shaderEffect;
    private float _randomValue = -1;
    private float _lyricRenderXOffset = 0;
    private float _lyricRenderYOffset = 0;
    private readonly Color _darkSpectrumColor = Color.FromArgb(32, 0, 0, 0);
    private readonly Color _lightSpectrumColor = Color.FromArgb(32, 255, 255, 255);
    public List<LyricItemModel> _lyricList = new();
    private LyricRenderView _lyricBox = new LyricRenderView();
    private Setting _settings;
    private List<Vector3> _albumColorVectors = new();
    private List<Color> _albumColors = new();
    private SolidColorBrush? _pureIdleBrushCache;
    private Color? _karaokAccentColorCache;
    private SolidColorBrush? _pureAccentBrushCache;

    public double LyricShowSize { get; set; }
    public double LyricWidth { get; set; }
    public string NowPlaybackSpeed
    {
        get => (string)GetValue(NowPlaybackSpeedProperty);
        set => SetValue(NowPlaybackSpeedProperty, value);
    }

    private ExpandedPlayerViewModel ViewModel => (ExpandedPlayerViewModel)DataContext;

    public ExpandedPlayer()
    {
        InitializeComponent();
        _settings = Ioc.Default.GetRequiredService<Setting>();
        DataContext = Ioc.Default.GetRequiredService<ExpandedPlayerViewModel>();
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
        _lyricBox.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        _lyricBox.OnBeforeRender += _lyricBox_OnBeforeRender;
        _lyricBox.OnLyricLineClicked += _lyricBoxOnOnRequestSeek;
        _lyricBox.Context.LyricWidthRatio = _settings.lyricRenderWidthRatio / 100f;
        _lyricBox.Context.LyricPaddingTopRatio = _settings.lyricPaddingTopRatio / 100f;
        _lyricBox.Context.CurrentLyricTime = 0;
        _lyricBox.Context.Debug = _settings.LyricRendererDebugMode;
        _lyricBox.Context.Effects.Blur = _settings.lyricRenderBlur;
        _lyricBox.Context.LineRollingEaseCalculator = _settings.LineRollingCalculator switch
        {
            1 => new SinRollingCalculator(),
            2 => new LyricifyRollingCalculator(),
            3 => new SyncRollingCalculator(),
            4 => new CircleEaseRollingCalculator(),
            _ => new ElasticEaseRollingCalculator()
        };
        _lyricBox.Context.Effects.ScaleWhenFocusing = _settings.lyricRenderScaleWhenFocusing;
        _lyricBox.Context.Effects.FocusHighlighting = _settings.lyricRenderFocusHighlighting;
        _lyricBox.Context.Effects.TransliterationScanning = _settings.lyricRenderTransliterationScanning;
        _lyricBox.Context.Effects.SimpleLineScanning = _settings.lyricRenderSimpleLineScanning;
        _lyricBox.Context.PreferTypography.Font = _settings.lyricFontFamily;
        _lyricBox.Context.LineSpacing = _settings.lyricLineSpacing;
    }

    private void HyPlayList_OnManualSeek(TimeSpan position)
    {
        _positionChangedBySeeking = true;
    }

    private void _lyricBoxOnOnRequestSeek(RenderingLyricLine line)
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

    private void _lyricBox_OnBeforeRender(LyricRenderer.LyricRenderView view)
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
            _lyricBox.ReflowTime(0);
        }
        else
        {
            view.Context.CurrentLyricTime = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds;
        }
        view.Context.IsSeek = _positionChangedBySeeking;
        _positionChangedBySeeking = false;

    }

    public void SingleViewModeToggle()
    {
        if (_windowMode == ExpandedWindowMode.Both) return;
        else
        {
            _windowMode = _windowMode == ExpandedWindowMode.LyricOnly ? ExpandedWindowMode.CoverOnly : ExpandedWindowMode.LyricOnly;
            Change_windowMode();
        }
    }
    private void HyPlayList_OnPlay()
    {
        _ = Common.Invoke(() =>
        {
            if (_settings.albumRotate)
                //网易云音乐圆形唱片
                RotateAnimationSet.StartAsync();
            if (_settings.expandAlbumBreath)
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
            if (_settings.albumRotate)
                RotateAnimationSet.Stop();
            if (_settings.expandAlbumBreath)
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
        if (_needRedesign > 0)
        {
            _needRedesign--;
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
        if (_settings.albumRotate)
            RotateAnimationSet.Stop();
        if (_settings.expandAlbumBreath)
        {
            ImageAlbumAni?.Stop();
        }
    }

    private void HyPlayList_OnLyricLoaded()
    {
        LoadLyricsBox();
        _needRedesign++;
    }

    private void Current_SizeChanged(object? sender, WindowSizeChangedEventArgs? e)
    {
        _nowWidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
        _nowHeight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
        if (_lastWidth != _nowWidth)
        {
            //这段不要放出去了
            if (_windowMode == ExpandedWindowMode.Both)
                LyricWidth = _nowWidth * 0.5;
            else
                LyricWidth = _nowWidth - 15;
            LyricWidth = Math.Max(LyricWidth, 0);
            LyricShowSize = _settings.lyricSize <= 0
                ? Math.Max(_nowWidth / 40, 40)
                : _settings.lyricSize;

            _lastWidth = _nowWidth;
            _needRedesign += 2;
        }
        else if (_lastHeight != _nowHeight)
        {
            _lastHeight = _nowHeight;
            _needRedesign += 2;
        }
    }

    private void Change_windowMode()
    {
        _isRealClick = false;

        if (_windowMode == ExpandedWindowMode.Both)
            LyricWidth = _nowWidth * 0.5;
        else
            LyricWidth = _nowWidth - 30;
        LyricWidth = Math.Max(LyricWidth, 0);

        switch (_windowMode)
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

        _needRedesign++;
        _isRealClick = true;
    }

    private void Redesign()
    {
        if (_needRedesign > 5) _needRedesign = 5;
        // 这个函数里面放无法用XAML实现的页面布局方式


        if (600 > Math.Min(LeftPanel.ActualHeight, MainGrid.ActualHeight))
        {
            SongInfo.Width = ImageAlbum.Width;
        }
        else
        {
            ImageAlbum.Width = double.NaN;
            ImageAlbum.Height = double.NaN;
            SongInfo.Width = double.NaN;
        }

        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;

        float sizey = 1;
        float sizex = 1;
        if (_windowMode != ExpandedWindowMode.LyricOnly)
        {
            if (SongInfo.ActualOffset.Y + SongInfo.ActualHeight > MainGrid.ActualHeight)
                sizey = (float)(MainGrid.ActualHeight / (SongInfo.ActualOffset.Y + SongInfo.ActualHeight));

            if (ImageAlbum.ActualOffset.X + ImageAlbum.ActualWidth > LeftPanel.ActualWidth)
                sizex = (float)(LeftPanel.ActualWidth / (ImageAlbum.ActualOffset.X + ImageAlbum.ActualWidth));
            UIAugmentationSys.ChangeView(0, 0, Math.Min(sizex, sizey));
        }
        if (Math.Abs(lastChangedLyricWidth - LyricWidth) > 0.001f && Math.Abs(_lyricRenderXOffset - RightPanel.ActualOffset.X) > 0.001f)
        {
            _lyricRenderXOffset = RightPanel.ActualOffset.X;
            _lyricRenderYOffset = RightPanel.ActualOffset.Y;
            _lyricBox.Redesign((float)LyricWidth, _nowHeight);
            _lyricBox.ChangeRenderFontSize((float)LyricShowSize,
                (_settings.translationSize > 0) ? _settings.translationSize : (float)LyricShowSize / 2,
                (_settings.romajiSize > 0) ? _settings.romajiSize : (float)LyricShowSize / 2);
            lastChangedLyricWidth = LyricWidth;
        }

        //歌词宽度
        if (_nowWidth <= 800)
        {
            if (!_isManualChangeMode && _windowMode == ExpandedWindowMode.Both)
            {
                _windowMode = ExpandedWindowMode.CoverOnly;
                Change_windowMode();
            }
        }
        else if (_nowWidth > 800)
        {
            if (!_isManualChangeMode && _windowMode != ExpandedWindowMode.Both)
            {
                _windowMode = ExpandedWindowMode.Both;
                Change_windowMode();
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
        _isProgramClick = true;
        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;
        _isProgramClick = false;
        try
        {
            OnSongChange(HyPlayList.List[HyPlayList.NowPlaying]);
            RefreshAlbumCover(HyPlayList.NowPlayingItem);
            Change_windowMode();
            _needRedesign++;
        }
        catch
        {
        }

        if (_settings.expandedPlayerBackgroundType == 0 && !_settings.expandedUseAcrylic)
            AcrylicCover.Fill = new BackdropBlurBrush { Amount = 50.0 };
        if (_settings.expandedPlayerBackgroundType == BackgroundType.Animated)
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
        if (_settings.expandedPlayerBackgroundType == BackgroundType.DesktopAcrylic)
            PageContainer.Background =
                (Brush)new BooleanToWindowBrushesConverter().Convert(
                    _settings.acrylicBackgroundStatus, null, null,
                    null);

        NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        if (_settings.pureLyricFocusingColor is not null)
        {
            _pureAccentBrushCache ??= Common.BrushManagement.AccentBrush;
        }
        if (_settings.pureLyricIdleColor is not null)
        {
            _pureIdleBrushCache ??= Common.BrushManagement.IdleBrush;
        }
        if (_settings.karaokLyricFocusingColor is not null)
        {
            _karaokAccentColorCache ??= Common.BrushManagement.KaraokAccentBrush;
        }
    }


    private Storyboard bpmAniStoryboard = new Storyboard();

    public void LoadLyricsBox()
    {
        _ = Common.Invoke(() =>
        {
            if (_lyricIsCleaning) return;
            if (HyPlayList.HyLyricInfo.PureLyricInfo is not HyALRCLyricInfo alrcLyricInfo)
            {
                _lyricBox.SetLyricLines(LrcConverter.Convert(ConvertToALRC(HyPlayList.HyLyricInfo.Lyrics), HyPlayList.HyLyricInfo.LyricMetadata, HyPlayList.HyLyricInfo.SongMetadata));
            }
            else
            {
                _lyricBox.SetLyricLines(LrcConverter.Convert(alrcLyricInfo.ALRC, alrcLyricInfo.LyricMetadata, alrcLyricInfo.SongMetadata));
            }
            _lyricBox.ChangeAlignment(_settings.lyricAlignment switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left
            });
            _lyricBox.ReflowTime(0);
            if (HyPlayList.NowPlayingItem == null) return;
            _lyricBox.Redesign((float)LyricWidth, _nowHeight);
            _lyricBox.ChangeRenderColor(Common.BrushManagement.IdleBrush.Color, Common.BrushManagement.AccentBrush.Color);
            Redesign();
            _lyricHasBeenLoaded = true;
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
        var lyricIsReady = _lastSong == HyPlayList.NowPlayingItem;
        _lyricHasBeenLoaded = lyricIsReady;
        _ = Common.Invoke(() =>
        {
            var artistText = mpi?.PlayItem?.ArtistString;
            ViewModel.Artist = artistText;
            ViewModel.SongName = mpi?.PlayItem?.Name;
            ViewModel.Album = mpi?.PlayItem?.AlbumString;
            if (mpi?.PlayItem == null)
            {
                _lyricList.Clear();
            }

            if (mpi?.PlayItem == null) return;

            if (!lyricIsReady)
            {
                if (!_lyricHasBeenLoaded)
                {
                    //歌词加载中提示
                    _lyricIsCleaning = true;
                    lock (_lyricList)
                    {
                        _lyricList.Clear();
                        _lyricList.Add(new LyricItemModel(SongLyric.LoadingLyric));
                    }

                    _lyricBox.Redesign((float)LyricWidth, _nowHeight);
                    _lyricIsCleaning = false;
                    if (_lyricHasBeenLoaded)
                    {
                        LoadLyricsBox();
                    }
                }
            }

            _needRedesign++;
            NowPlaybackSpeed = "x" + HyPlayList.Player.GetPlaybackSourceSpeed(HyPlayList.NowPlayingItem.PlayItem.AudioGraphPlaybackSource);
        });
    }

    public void RefreshUIColor()
    {
        _lyricBox.ChangeRenderColor(Common.BrushManagement.IdleBrush.Color, Common.BrushManagement.AccentBrush.Color);
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
            if (_settings.expandAnimation &&
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
        _lyricBox.LyricView_OnPointerWheelChanged(sender, e);
    }

    private void ToggleButtonTranslation_OnClick(object sender, RoutedEventArgs e)
    {
        Common.ShowLyricTrans = ToggleButtonTranslation.IsChecked;
        if (_lyricBox != null)
        {
            _lyricBox.EnableTranslation = Common.ShowLyricTrans;
        }

    }

    private void ToggleButtonSound_OnClick(object sender, RoutedEventArgs e)
    {
        Common.ShowLyricSound = ToggleButtonSound.IsChecked;
        if (_lyricBox != null)
        {
            _lyricBox.EnableTransliteration = Common.ShowLyricSound;
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

            if (_settings.forceMemoryGarbage)
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

            if (_settings.forceMemoryGarbage)
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
        if (!_isRealClick) return;
        _isManualChangeMode = true;
        if (BtnToggleAlbum.IsChecked && BtnToggleLyric.IsChecked)
            _windowMode = ExpandedWindowMode.Both;
        else if (BtnToggleAlbum.IsChecked)
            _windowMode = ExpandedWindowMode.CoverOnly;
        else if (BtnToggleLyric.IsChecked) _windowMode = ExpandedWindowMode.LyricOnly;
        Change_windowMode();
    }

    private void BtnToggleFullScreen_Checked(object sender, RoutedEventArgs e)
    {
        if (_isProgramClick) return;
        if (BtnToggleFullScreen.IsChecked)
        {
            ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            Change_windowMode();
        }
        else if (ApplicationView.GetForCurrentView().IsFullScreenMode)
        {
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
            Change_windowMode();
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
        //_lyricBox.ContextFlyout.ShowAt(_lyricBox);
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
            _lyricBox.SetLyricLines(lrcs);
        }
    }

    private async void LyricBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_windowMode == ExpandedWindowMode.LyricOnly)
        {
            UISettings _uiSettings = new UISettings();
            await Task.Delay((int)(_uiSettings.DoubleClickTime + 55));
            if (!_lyricBox.HasJumpedLyrics)
            {
                _windowMode = ExpandedWindowMode.CoverOnly;
                Change_windowMode();
            }
        }
    }

    private async Task<bool> IsBrightAsync(IRandomAccessStream stream)
    {
        _lastSong = HyPlayList.NowPlayingItem;
        var finalResult = false; //在不手动指定背景类型为2至5时需要执行颜色采样
        var resultGenerated = false; //标志返回颜色已经生成
        if (_settings.lyricColor != 0 && _settings.lyricColor != 3)
        {
            finalResult = _settings.lyricColor == 2;
            resultGenerated = true;
        }
        if (HyPlayList.NowPlayingItem.PlayItem == null) return false;
        if (_settings.expandedPlayerBackgroundType == BackgroundType.DesktopAcrylic)
        {
            return ActualTheme == ElementTheme.Light;
        }
        try
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            var colors = await ImageDecoder.GetPixelColor(decoder);
            ThemeColorResult themeColor;
            PaletteResult palette;
            if (_settings.expandedPlayerBackgroundType != BackgroundType.Animated && _settings.expandedPlayerBackgroundType != BackgroundType.Isolation)
            {
                switch (_settings.ColorGeneratorType)
                {
                    case 0:
                    case 2:
                    default:
                        themeColor = await PaletteGenerators.KMeansPaletteGenerator.CreateThemeColor(colors, _settings.ImpressionistIgnoreWhite, _settings.ImpressionistLABSpace);
                        break;
                    case 1:
                        themeColor = await PaletteGenerators.OctTreePaletteGenerator.CreateThemeColor(colors, _settings.ImpressionistIgnoreWhite);
                        break;
                }
                albumMainColor = Color.FromArgb(255, (byte)themeColor.Color.X, (byte)themeColor.Color.Y, (byte)themeColor.Color.Z);
            }
            else
            {
                switch (_settings.ColorGeneratorType)
                {
                    case 0:
                        palette = await PaletteGenerators.KMeansPaletteGenerator.CreatePalette(
                            colors,
                            _settings.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                            _settings.ImpressionistIgnoreWhite,
                            _settings.ImpressionistLABSpace,
                            _settings.ImpressionistUseKMeansPP);
                        break;
                    case 1:
                        palette = palette = await PaletteGenerators.OctTreePaletteGenerator.CreatePalette(
                            colors,
                            _settings.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                            _settings.ImpressionistIgnoreWhite);
                        break;
                    case 2:
                    default:
                        palette = await AutoPaletteGenerator.CreatePalette(
                            colors,
                            _settings.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                            _settings.ImpressionistIgnoreWhite,
                            _settings.ImpressionistLABSpace,
                            _settings.ImpressionistUseKMeansPP);
                        break;
                }
                themeColor = palette.ThemeColor;
                _albumColors = palette.Palette.Select(quantizedColor => Color.FromArgb(255, (byte)quantizedColor.X, (byte)quantizedColor.Y, (byte)quantizedColor.Z))
                    .ToList();
                albumMainColor = Color.FromArgb(255, (byte)themeColor.Color.X, (byte)themeColor.Color.Y, (byte)themeColor.Color.Z);
                _albumColorVectors = palette.Palette.Select(t => t / 255).ToList();
            }
            if (_settings.expandedPlayerBackgroundType is BackgroundType.CoverTheme)
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
        _settings.ABStartPoint = HyPlayList.Player.PrimaryAudioInputNode.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ABEndPoint = HyPlayList.Player.PrimaryAudioInputNode.Position;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Common.PageMain!.IsExpandedPlayerInitialized = true;
        ToggleButtonSound.IsChecked = Common.ShowLyricSound;
        ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
        if (_settings.albumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(_settings.albumBorderLength);
        switch (_settings.expandedPlayerBackgroundType)
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

        if (_settings.albumRotate)
            //网易云音乐圆形唱片
            if (HyPlayList.IsPlaying)
                _ = RotateAnimationSet.StartAsync();
        if (_settings.expandAlbumBreath)
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
        if (e.PointerDeviceType == PointerDeviceType.Mouse || !_settings.enableTouchGestureAction) return;
        double manipulationDeltaRotateValue;
        switch (_settings.gestureMode)
        {
            case 3:
                if (!_settings.albumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                HyPlayList.Seek(HyPlayList.Player.PrimaryAudioInputNode.Position.Add(
                    TimeSpan.FromMilliseconds((int)manipulationDeltaRotateValue) * 100));
                break;
            case 2:
                if (!_settings.albumRound) return;
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
        if (_settings.gestureMode == 0)
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

    public static Color AdjustBrightness(Color color, float percentage)
    {
        int adjustment = (int)(255 * percentage);
        int r = Math.Max(0, Math.Min(255, color.R + adjustment));
        int g = Math.Max(0, Math.Min(255, color.G + adjustment));
        int b = Math.Max(0, Math.Min(255, color.B + adjustment));
        return Color.FromArgb(color.A, (byte)r, (byte)g, (byte)b);
    }


    public async void RefreshAlbumCover(HyPlayItem playItem)
    {
        if (HyPlayList.CoverStream == null) return;
        _ = Common.Invoke(async () =>
        {
            if (!_settings.noImage)
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
                    ViewModel.Cover = bitmap;
                    if (_settings.expandedPlayerBackgroundType == 0 && Background is not ImageBrush)
                    {
                        var brush = new ImageBrush()
                        { Stretch = Stretch.UniformToFill };
                        Background = brush;
                    }
                    if (Background is ImageBrush imageBrush)
                    {
                        imageBrush.ImageSource = bitmap;
                    }

                    if (playItem != HyPlayList.NowPlayingItem) return;
                    if (albumMainColor != null)
                    {
                        var coverColor = albumMainColor.Value;
                    }
                    if (_settings.expandedPlayerBackgroundType == BackgroundType.Animated && isBright)
                        BlackCover.Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                    else if (_settings.expandedPlayerBackgroundType == BackgroundType.Animated && !isBright)
                        BlackCover.Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
                    if (_settings.lyricColor != 3 || albumMainColor == null)
                    {
                        if (isBright)
                        {
                            Common.BrushManagement.AccentBrush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                            Common.BrushManagement.IdleBrush = new SolidColorBrush(Color.FromArgb(114, 0, 0, 0));
                        }
                        else
                        {
                            Common.BrushManagement.AccentBrush =
                                new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                            Common.BrushManagement.IdleBrush = new SolidColorBrush(Color.FromArgb(66, 255, 255, 255));
                        }
                    }
                    else
                    {
                        if (_settings.expandedPlayerBackgroundType != 0)
                        {
                            if (isBright)
                            {
                                var AccentColor = AdjustBrightness((Color)albumMainColor, -0.3f);
                                Common.BrushManagement.AccentBrush = new SolidColorBrush(AccentColor);
                                var idleColor = AccentColor;
                                idleColor.A = 150;
                                Common.BrushManagement.IdleBrush = new SolidColorBrush(idleColor);

                            }
                            else
                            {
                                var AccentColor = AdjustBrightness((Color)albumMainColor, 0.3f);
                                Common.BrushManagement.AccentBrush = new SolidColorBrush(AccentColor);
                                var idleColor = AdjustBrightness((Color)AccentColor, -0.15f);
                                idleColor.A = 150;
                                Common.BrushManagement.IdleBrush = new SolidColorBrush(idleColor);
                            }
                        }
                        else
                        {
                            var AccentColor = AdjustBrightness((Color)albumMainColor, -0.3f);
                            Common.BrushManagement.AccentBrush = new SolidColorBrush(AccentColor);
                            var idleColor = AccentColor;
                            idleColor.A = 150;
                            Common.BrushManagement.IdleBrush = new SolidColorBrush(idleColor);
                        }
                    }



                    if (_settings.playbarBackgroundElay)
                        Common.BarPlayBar!.SetPlayBarIdleBackground(Common.BrushManagement.IdleBrush);
                    //LoadLyricsBox();
                    RefreshUIColor();
                    if (_settings.expandedPlayerBackgroundType == BackgroundType.Animated)
                    {
                        BgRect00.Fill = new SolidColorBrush(_albumColors[0]);
                        BgRect01.Fill = new SolidColorBrush(_albumColors[1]);
                        BgRect02.Fill = new SolidColorBrush(_albumColors[2]);
                        BgRect10.Fill = new SolidColorBrush(_albumColors[3]);
                        BgRect11.Fill = new SolidColorBrush(_albumColors[4]);
                        BgRect12.Fill = new SolidColorBrush(_albumColors[5]);
                        BgRect20.Fill = new SolidColorBrush(_albumColors[6]);
                        BgRect21.Fill = new SolidColorBrush(_albumColors[7]);
                        BgRect22.Fill = new SolidColorBrush(_albumColors[8]);
                    }
                    if (_settings.expandedPlayerBackgroundType == BackgroundType.Isolation)
                    {
                        if (_shaderEffect != null)
                        {
                            _shaderEffect.Properties["color1"] = _albumColorVectors[0];
                            _shaderEffect.Properties["color2"] = _albumColorVectors[1];
                            _shaderEffect.Properties["color3"] = _albumColorVectors[2];
                            _shaderEffect.Properties["color4"] = _albumColorVectors[3];
                            _shaderEffect.Properties["UseHSVBlending"] = UseHSVBlending();
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
        _shaderEffect = null;
    }
    public Task Show()
    {
        _stopwatch.Reset();
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
        _stopwatch.Start();
        await Task.Run(async () =>
        {
            while (_stopwatch.ElapsedMilliseconds < 3000)
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
        _stopwatch.Stop();
    }

    private void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!_settings.AutoHidePlaybar) return;
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
        if (_shaderEffect == null && _settings.expandedPlayerBackgroundType == BackgroundType.Isolation)
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
        }
        LuminousBackground.DpiScale = _settings.IsolationScale;
        _shaderEffect?.Properties["Width"] = (float)LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualWidth, Microsoft.Graphics.Canvas.CanvasDpiRounding.Round);
        _shaderEffect?.Properties["Height"] = (float)LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualHeight, Microsoft.Graphics.Canvas.CanvasDpiRounding.Round);
        if (!_settings.IsolationFullThrottle)
        {
            LuminousBackground.IsFixedTimeStep = true;
            LuminousBackground.TargetElapsedTime = TimeSpan.FromMilliseconds(16.6 * (60d / _settings.IsolationFPS));
        }
    }

    private void LuminousBackground_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
    {
        if (HyPlayList.IsPlaying && _settings.expandedPlayerBackgroundType == BackgroundType.Isolation)
        {
            var progress = (float)args.Timing.TotalTime.TotalSeconds + _randomValue;
            _shaderEffect?.Properties["iTime"] = progress;
        }
    }
    private bool UseHSVBlending()
    {
        var hueList = _albumColorVectors.Select(t => t.RGBVectorToHSVColor().H).ToList();
        var avg = hueList.Average();
        var sum = hueList.Sum(d => Math.Pow(d - avg, 2));
        var variance = Math.Sqrt(sum / 4);
        return variance <= 90;
    }
    private void LuminousBackground_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
    {
        using var session = args.DrawingSession;
        if (_shaderEffect != null && _settings.expandedPlayerBackgroundType == BackgroundType.Isolation)
        {
            session.DrawImage(_shaderEffect);
        }
        if (_settings.EnableFFT)
        {
            DrawAudioFFTGraph(sender, session);
        }
        using var lyricCommand = new CanvasCommandList(session);
        using var lyricSession = lyricCommand.CreateDrawingSession();
        _lyricBox.Draw(lyricSession, args.Timing); ;
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
                Common.BrushManagement.IsBright ? _darkSpectrumColor : _lightSpectrumColor);
        }
    }

    private void LeftPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_windowMode == ExpandedWindowMode.Both) return;
        else
        {
            _windowMode = _windowMode == ExpandedWindowMode.LyricOnly ? ExpandedWindowMode.CoverOnly : ExpandedWindowMode.LyricOnly;
            Change_windowMode();
        }
    }

    private void LyricView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _lyricBox.OnDoubleTapped(sender, e);
    }

    private void LyricView_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _lyricBox.LyricView_OnPointerExited(sender, e);
    }

    private void LyricView_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _lyricBox.LyricView_OnPointerMoved(sender, e);
    }

    private void LyricView_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _lyricBox.LyricView_OnPointerPressed(sender, e);
    }

    private void LyricView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _lyricBox.LyricView_PointerReleased(sender, e);
    }
}
