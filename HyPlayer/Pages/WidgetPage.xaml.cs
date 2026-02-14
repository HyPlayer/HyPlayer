using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinRT;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

public sealed partial class WidgetPage : Page
{
    private XboxGameBarWidget _widget;
    private XboxGameBarHotkeyWatcher _hotkeyWatcher;
    private GameBarSettings _settings;
    private LyricRenderView LyricBox = new LyricRenderView();


    public WidgetPage()
    {
        this.InitializeComponent();
        _settings = new GameBarSettings(Dispatcher);
        Instance = this;
    }
#nullable enable
    public static WidgetPage? Instance { get; private set; }
#nullable restore


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _widget = e.Parameter?.As<XboxGameBarWidget>();
        Common.XboxGameBarWidget = _widget;
        if (HyPlayList.NowPlayingItem.PlayItem is null) return;
        Initialize();
    }

    private async void OnSettingsChecked(XboxGameBarWidget sender, object args)
    {
        await sender.ActivateSettingsAsync();
    }

    private void FindLyricButton_Click(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.PlayItem is null) return;
        Initialize();
    }

    public void Initialize()
    {
        _hotkeyWatcher = XboxGameBarHotkeyWatcher.CreateWatcher(_widget, (List<VirtualKey>)[VirtualKey.Control, VirtualKey.LeftMenu, VirtualKey.A]);//全局热键
        _hotkeyWatcher.Start();
        InitializeLyricView();
        _widget.CloseRequested += Widget_CloseRequested;
        _widget.SettingsClicked += OnSettingsChecked;
        _widget.WindowBoundsChanged += OnResized;
        _widget.RequestedThemeChanged += RequestedThemeChanged;
        _hotkeyWatcher.HotkeySetStateChanged += OnHotkeySetStateChanged;
        HyPlayList.OnPlayItemChange += HyPlayList_OnPlayItemChange;
        HyPlayList.OnPlayPositionChange += HyPlayList_OnPlayPositionChange;
        HyPlayList.OnPause += HyPlayList_OnPause;
        HyPlayList.OnPlay += HyPlayList_OnPlay;
        HyPlayList.OnLyricLoaded += OnPlaylistLyricLoaded;
        TipContent.Visibility = Visibility.Collapsed;
        LyricBox.Context.Debug = Common.Setting.LyricRendererDebugMode;
        PlayStateIcon.Glyph =
                    HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
                        ? "\uF8AE"
                        : "\uF5B0";
        LoadLyrics();
    }

    private void HyPlayList_OnPlay()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            PlayStateIcon.Glyph = HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing ? "\uF8AE" : "\uF5B0";
        });
    }

    private void HyPlayList_OnPause()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            PlayStateIcon.Glyph = HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing ? "\uF8AE" : "\uF5B0";
        });
    }

    private void RequestedThemeChanged(XboxGameBarWidget sender, object args)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            this.RequestedTheme = _widget.RequestedTheme;
            LyricBox.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color, Colors.Black);
        });
    }

    private void Widget_CloseRequested(XboxGameBarWidget sender, XboxGameBarWidgetCloseRequestedEventArgs args)
    {
        Debug.WriteLine("GameBar Close Requested.");
        _hotkeyWatcher.Stop();
        _widget.CloseRequested -= Widget_CloseRequested;
        _widget.SettingsClicked -= OnSettingsChecked;
        _widget.WindowBoundsChanged -= OnResized;
        _widget.RequestedThemeChanged -= RequestedThemeChanged;
        _hotkeyWatcher.HotkeySetStateChanged -= OnHotkeySetStateChanged;
        HyPlayList.OnPlayItemChange -= HyPlayList_OnPlayItemChange;
        HyPlayList.OnPlayPositionChange -= HyPlayList_OnPlayPositionChange;
        HyPlayList.OnPause -= HyPlayList_OnPause;
        HyPlayList.OnPlay -= HyPlayList_OnPlay;
        HyPlayList.OnLyricLoaded -= OnPlaylistLyricLoaded;
        Common.XboxGameBarWidget = null;
        Instance = null;
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            LyricView.RemoveFromVisualTree();
        });

    }

    private void HyPlayList_OnPlayPositionChange(TimeSpan position)
    {
        if (HyPlayList.NowPlayingItem.PlayItem == null) return;
        var progress = position.TotalMilliseconds / HyPlayList.NowPlayingItem.LengthInMilliseconds * 100;
        var text = $"{position.ToString(@"mm\:ss")}/{TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.LengthInMilliseconds).ToString(@"mm\:ss")}";
        _ = Dispatcher.RunAsync(
            CoreDispatcherPriority.Normal,
            () =>
            {
                PositionProgressBar.Value = progress;
                CurrentPositionText.Text = text;
            });
    }

    private void HyPlayList_OnPlayItemChange(HyPlayItem playItem)
    {
        var playItemName = HyPlayList.NowPlayingItem.Name;
        var artistName = HyPlayList.NowPlayingItem.ArtistString;
        _ = Dispatcher.RunAsync(
           CoreDispatcherPriority.Normal,
           () =>
           {
               SongNameText.Text = playItemName;
               ArtistText.Text = artistName;
           });
    }

    private void MovePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.SongMovePrevious();
    }

    private void MoveNextButton_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.SongMoveNext();
    }

    private void ChangePlayStateButton_Click(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.IsPlaying) HyPlayList.Player.PauseAll();
        else HyPlayList.Player.PlayAll();
    }

    private void WidgetPage_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        BorderBackground.Visibility = Visibility.Collapsed;
        PlayBar.Visibility = Visibility.Collapsed;
    }

    private void WidgetPage_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BorderBackground.Visibility = Visibility.Visible;
        PlayBar.Visibility = Visibility.Visible;
    }

    private void OnPlaylistLyricLoaded()
    {
        LoadLyrics();
    }

    private void OnResized(XboxGameBarWidget sender, object args)
    {
        UpdateLyricSize();
    }

    private void OnHotkeySetStateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
    {
        if (args.HotkeySetDown)
        {
            if (HyPlayList.IsPlaying) HyPlayList.Player.PauseAll();
            else HyPlayList.Player.PlayAll();
        }
    }

    public void UpdateLyricViewSettings()
    {
        LyricBox.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        LyricBox.OnBeforeRender += LyricBox_OnBeforeRender;
        LyricBox.OnLyricLineClicked += LyricView_OnRequestSeek;
        LyricBox.Context.LyricPaddingTopRatio = Common.Setting.lyricPaddingTopRatio / 100f;
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
        LyricBox.Context.PreferTypography.Font = _settings.LyricFontFamily;
        LyricBox.Context.LineSpacing = _settings.LyricLineSpacing;
        LyricBox.EnableTranslation = _settings.EnableTranslation;
        LyricBox.EnableTransliteration = _settings.EnableTransliteration;
        LyricBox.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color, Colors.Black);
        UpdateLyricSize();
    }

    private void LyricBox_OnBeforeRender(LyricRenderView view)
    {
        if (HyPlayList.Player.PrimaryAudioInputNode == null) return;
        LyricBox.Context.IsPlaying = HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing;
        if (HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds < LyricBox.Context.CurrentLyricTime)
        {
            LyricBox.Context.CurrentLyricTime = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds;
            LyricBox.ReflowTime(0);
        }
        else
        {
            LyricBox.Context.CurrentLyricTime = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds;
        }
    }

    private void InitializeLyricView()
    {
        LyricBox.Context.CurrentLyricTime = 0;
        LyricBox.Context.LyricWidthRatio = 1;
        UpdateLyricViewSettings();
        HyPlayList_OnPlayItemChange(null);
    }

    private void UpdateLyricSize()
    {
        if (HyPlayList.NowPlayingItem == null) return;
        var lyricSize = _settings.LyricSize <= 0
            ? Math.Max(_widget.WindowBounds.Width / 20, 40)
            : _settings.LyricSize;
        var translationSize = (_settings.TranslationSize > 0) ? _settings.TranslationSize : lyricSize / 1.8;
        var romajiSize = (_settings.RomajiSize > 0) ? _settings.RomajiSize : lyricSize / 2;

        LyricBox.ChangeRenderFontSize((float)lyricSize, (float)translationSize, (float)romajiSize);
        LyricBox.ChangeAlignment(_settings.LyricAlignment switch
        {
            1 => TextAlignment.Center,
            2 => TextAlignment.Right,
            _ => TextAlignment.Left
        });
    }

    private void LyricView_OnRequestSeek(RenderingLyricLine line)
    {
        HyPlayList.Seek(TimeSpan.FromMilliseconds(line.StartTime));
    }

    private void LoadLyrics()
    {
        LyricBox.SetLyricLines(LrcConverter.Convert(ExpandedPlayer.ConvertToALRC(HyPlayList.HyLyricInfo.Lyrics)));
        LyricBox.ReflowTime(0);
    }

    private SolidColorBrush GetAccentBrush()
    {
        return Resources["AccentBrush"]?.As<SolidColorBrush>();
    }

    private SolidColorBrush GetIdleBrush()
    {
        return Resources["IdleBrush"]?.As<SolidColorBrush>();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _widget.Close();
    }

    private void LyricView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        LyricBox.OnDoubleTapped(sender, e);
    }

    private void LyricView_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
    {
        using var lyricSession = args.DrawingSession;
        LyricBox.Draw(lyricSession, args.Timing);
    }

    private void LyricView_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerExited(sender, e);
    }

    private void LyricView_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerMoved(sender, e);
    }

    private void LyricView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerPressed(sender, e);
    }

    private void LyricView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_PointerReleased(sender, e);
    }

    private void LyricView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        LyricBox.LyricView_OnPointerWheelChanged(sender, e);
    }

    private void LyricView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LyricBox.Redesign((float)e.NewSize.Width, (float)e.NewSize.Height);
    }
}