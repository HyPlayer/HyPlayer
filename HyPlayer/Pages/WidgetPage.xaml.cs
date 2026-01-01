using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using System;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

public sealed partial class WidgetPage : Page
{
    private XboxGameBarWidget _widget;
    private XboxGameBarHotkeyWatcher _hotkeyWatcher;


    public WidgetPage()
    {
        this.InitializeComponent();
        _settings = new GameBarSettings(Dispatcher);
        Instance = this;
    }
    private GameBarSettings _settings;

    public static WidgetPage Instance { get; private set; }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        FindLyricButton.Click += FindLyricButton_Click;
        _widget = e.Parameter as XboxGameBarWidget;
        Common.XboxGameBarWidget = _widget;
        _widget.SettingsClicked += OnSettingsChecked;
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
        HyPlayList.OnLyricLoaded += OnPlaylistLyricLoaded;

        _widget.WindowBoundsChanged += OnResized;
        _widget.RequestedThemeChanged += RequestedThemeChanged;
        _hotkeyWatcher = XboxGameBarHotkeyWatcher.CreateWatcher(_widget, [VirtualKey.Control, VirtualKey.LeftMenu, VirtualKey.A]);//全局热键
        _hotkeyWatcher.Start();
        _hotkeyWatcher.HotkeySetStateChanged += OnHotkeySetStateChanged;
        InitializeLyricView();
        PlayStateIcon.Glyph =
                    HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
                        ? "\uF8AE"
                        : "\uF5B0";
        LoadLyrics();
        this.PointerEntered += WidgetPage_PointerEntered;
        this.PointerExited += WidgetPage_PointerExited;
        HyPlayList.OnPlayItemChange += HyPlayList_OnPlayItemChange;
        HyPlayList.OnPlayPositionChange += HyPlayList_OnPlayPositionChange;
        ChangePlayStateButton.Click += ChangePlayStateButton_Click;
        MoveNextButton.Click += MoveNextButton_Click;
        MovePreviousButton.Click += MovePreviousButton_Click;
        TipContent.Visibility = Visibility.Collapsed;
        _widget.CloseRequested += Widget_CloseRequested;
        LyricView.Context.Debug = Common.Setting.LyricRendererDebugMode;
    }

    private void RequestedThemeChanged(XboxGameBarWidget sender, object args)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            this.RequestedTheme = _widget.RequestedTheme;
            LyricView.RequestedTheme = _widget.RequestedTheme;
            LyricView.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color, Colors.Black);
        });
    }

    private void Widget_CloseRequested(XboxGameBarWidget sender, XboxGameBarWidgetCloseRequestedEventArgs args)
    {
        HyPlayList.OnLyricLoaded -= OnPlaylistLyricLoaded;

        _widget.WindowBoundsChanged -= OnResized;
        _widget.RequestedThemeChanged -= RequestedThemeChanged;
        _widget.CloseRequested -= Widget_CloseRequested;
        _widget.SettingsClicked -= OnSettingsChecked;
        _hotkeyWatcher.HotkeySetStateChanged -= OnHotkeySetStateChanged;
        _hotkeyWatcher.Stop();

        HyPlayList.OnPlayItemChange -= HyPlayList_OnPlayItemChange;
        HyPlayList.OnPlayPositionChange -= HyPlayList_OnPlayPositionChange;
        
        if (LyricView != null)
        {
            LyricView.OnBeforeRender -= LyricView_OnBeforeRender;
            LyricView.OnLyricLineClicked -= LyricView_OnRequestSeek;
        }
        
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            PointerEntered -= WidgetPage_PointerEntered;
            PointerExited -= WidgetPage_PointerExited;
            ChangePlayStateButton.Click -= ChangePlayStateButton_Click;
            MoveNextButton.Click -= MoveNextButton_Click;
            MovePreviousButton.Click -= MovePreviousButton_Click;
            FindLyricButton.Click -= FindLyricButton_Click;
        });
    }

    private void HyPlayList_OnPlayPositionChange(TimeSpan position)
    {
        var progress = position.TotalMilliseconds / HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds * 100;
        var text = $"{position.ToString(@"mm\:ss")}/{TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds).ToString(@"mm\:ss")}";
        _ = Dispatcher.RunAsync(
            CoreDispatcherPriority.Normal,
            () =>
            {
                PositionProgressBar.Value = progress;
                CurrentPositionText.Text = text;
                PlayStateIcon.Glyph =
                    HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
                        ? "\uF8AE"
                        : "\uF5B0";
            });
    }

    private void HyPlayList_OnPlayItemChange(HyPlayItem playItem)
    {
        var playItemName = HyPlayList.NowPlayingItem.PlayItem.Name;
        var artistName = HyPlayList.NowPlayingItem.PlayItem.ArtistString;
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
        LyricView.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        LyricView.OnBeforeRender += LyricView_OnBeforeRender;
        LyricView.OnLyricLineClicked += LyricView_OnRequestSeek;
        LyricView.Context.LyricPaddingTopRatio = Common.Setting.lyricPaddingTopRatio / 100f;
        LyricView.Context.Debug = Common.Setting.LyricRendererDebugMode;
        LyricView.Context.Effects.Blur = Common.Setting.lyricRenderBlur;
        LyricView.Context.LineRollingEaseCalculator = Common.Setting.LineRollingCalculator switch
        {
            1 => new SinRollingCalculator(),
            2 => new LyricifyRollingCalculator(),
            3 => new SyncRollingCalculator(),
            4 => new CircleEaseRollingCalculator(),
            _ => new ElasticEaseRollingCalculator()
        };
        LyricView.Context.Effects.ScaleWhenFocusing = Common.Setting.lyricRenderScaleWhenFocusing;
        LyricView.Context.Effects.FocusHighlighting = Common.Setting.lyricRenderFocusHighlighting;
        LyricView.Context.Effects.TransliterationScanning = Common.Setting.lyricRenderTransliterationScanning;
        LyricView.Context.Effects.SimpleLineScanning = Common.Setting.lyricRenderSimpleLineScanning;
        LyricView.Context.PreferTypography.Font = _settings.LyricFontFamily;
        LyricView.Context.LineSpacing = _settings.LyricLineSpacing;
        LyricView.EnableTranslation = _settings.EnableTranslation;
        LyricView.EnableTransliteration = _settings.EnableTransliteration;
        LyricView.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color, Colors.Black);
        UpdateLyricSize();
    }


    private void InitializeLyricView()
    {
        LyricView.Context.CurrentLyricTime = 0;
        LyricView.Context.LyricWidthRatio = 1;
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
        LyricView.ChangeRenderFontSize((float)lyricSize, (float)translationSize, (float)romajiSize);
        LyricView.ChangeAlignment(_settings.LyricAlignment switch
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

    private void LyricView_OnBeforeRender(LyricRenderer.LyricRenderView view)
    {
        if (HyPlayList.Player.PrimaryAudioInputNode == null) return;
        view.Context.IsPlaying = HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing;
        if (HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds < view.Context.CurrentLyricTime)
        {
            view.Context.CurrentLyricTime = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds;
            LyricView.ReflowTime(0);
        }
        else
        {
            view.Context.CurrentLyricTime = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds;
        }
    }

    private void LoadLyrics()
    {
        //_lyricIsReadyToGo = true;
        //if (_lyricIsCleaning) return;
        LyricView.SetLyricLines(LrcConverter.Convert(ExpandedPlayer.ConvertToALRC(HyPlayList.LyricInfo.Lyrics)));
        LyricView.ReflowTime(0);
        //lastlrcid = HyPlayList.NowPlayingHashCode;

    }

    private SolidColorBrush GetAccentBrush()
    {
        return Resources["AccentBrush"] as SolidColorBrush;
    }

    private SolidColorBrush GetIdleBrush()
    {
        return Resources["IdleBrush"] as SolidColorBrush;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _widget.Close();
    }

}