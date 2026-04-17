using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin;
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
    private readonly GameBarSettings _settings;
    private readonly LyricRenderView LyricBox = new();

    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly ILyricService _lyricService = Ioc.Default.GetRequiredService<ILyricService>();


    public WidgetPage()
    {
        this.InitializeComponent();
        _settings = new GameBarSettings(Dispatcher);
        Instance = this;
        Window.Current.Closed += WidgetPage_Closed;
    }

    private void WidgetPage_Closed(object sender, CoreWindowEventArgs e)
    {
        UnregisterEvents();
    }
#nullable enable
    public static WidgetPage? Instance { get; private set; }
#nullable restore


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _widget = e.Parameter?.As<XboxGameBarWidget>();
        if (_state.NowPlayingItem is null) return;
        Initialize();
    }

    private async void OnSettingsChecked(XboxGameBarWidget sender, object args)
    {
        await sender.ActivateSettingsAsync();
    }

    private void FindLyricButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.NowPlayingItem is null) return;
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
        WeakReferenceMessenger.Default.Register<TrackChangedMessage>(this, (r, m) => HyPlayList_OnPlayItemChange(m.Item));
        WeakReferenceMessenger.Default.Register<PositionTickMessage>(this, (r, m) => HyPlayList_OnPlayPositionChange(m.Position));
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
        {
            if (m.IsPlaying) HyPlayList_OnPlay(); else HyPlayList_OnPause();
        });
        WeakReferenceMessenger.Default.Register<LyricLoadedMessage>(this, (r, m) => OnPlaylistLyricLoaded());
        TipContent.Visibility = Visibility.Collapsed;
        LyricBox.Context.Debug = Common.Setting.LyricRendererDebugMode;
        PlayStateIcon.Glyph =
                    _player.GlobalPlaybackStatus == PlaybackStatus.Playing
                        ? "\uF8AE"
                        : "\uF5B0";
        LoadLyrics();
    }

    private void HyPlayList_OnPlay()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            PlayStateIcon.Glyph = _player.GlobalPlaybackStatus == PlaybackStatus.Playing ? "\uF8AE" : "\uF5B0";
        });
    }

    private void HyPlayList_OnPause()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            PlayStateIcon.Glyph = _player.GlobalPlaybackStatus == PlaybackStatus.Playing ? "\uF8AE" : "\uF5B0";
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
        UnregisterEvents();
    }
    private void UnregisterEvents()
    {
        _hotkeyWatcher.Stop();
        _widget.CloseRequested -= Widget_CloseRequested;
        _widget.SettingsClicked -= OnSettingsChecked;
        _widget.WindowBoundsChanged -= OnResized;
        _widget.RequestedThemeChanged -= RequestedThemeChanged;
        _hotkeyWatcher.HotkeySetStateChanged -= OnHotkeySetStateChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Common.XboxGameBarWidget = null;
        Window.Current.Closed -= WidgetPage_Closed;
        Instance = null;
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            LyricView.RemoveFromVisualTree();
        });
    }
    private void HyPlayList_OnPlayPositionChange(TimeSpan position)
    {
        if (_state.NowPlayingItem == null) return;
        var progress = position.TotalMilliseconds / _state.NowPlayingItem.LengthInMilliseconds * 100;
        var text = $"{position:mm\\:ss}/{TimeSpan.FromMilliseconds(_state.NowPlayingItem.LengthInMilliseconds):mm\\:ss}";
        try
        {
            _ = Dispatcher.RunAsync(
            CoreDispatcherPriority.Normal,
            () =>
            {
                PositionProgressBar.Value = progress;
                CurrentPositionText.Text = text;
            });
        }
        catch
        {
            //Ignore
        }
    }

    private void HyPlayList_OnPlayItemChange(HyPlayItem playItem)
    {
        var playItemName = _state.NowPlayingItem.Name;
        var artistName = _state.NowPlayingItem.ArtistString;
        _ = Dispatcher.RunAsync(
           CoreDispatcherPriority.Normal,
           () =>
           {
               SongNameText.Text = playItemName;
               ArtistText.Text = artistName;
           });
    }

    private async void MovePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await _playlist.MovePreviousAsync();
    }

    private async void MoveNextButton_Click(object sender, RoutedEventArgs e)
    {
        await _playlist.MoveNextAsync(true);
    }

    private void ChangePlayStateButton_Click(object sender, RoutedEventArgs e)
    {
        _control.TogglePlayPause();
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
            _control.TogglePlayPause();
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
            RollingCalculator.SinRollingCalculator => new SinRollingCalculator(),
            RollingCalculator.LyricifyRollingCalculator => new LyricifyRollingCalculator(),
            RollingCalculator.SyncRollingCalculator => new SyncRollingCalculator(),
            RollingCalculator.CircleEaseRollingCalculator => new CircleEaseRollingCalculator(),
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
        if (_player.PrimaryAudioInputNode == null) return;
        LyricBox.Context.IsPlaying = _player.GlobalPlaybackStatus == PlaybackStatus.Playing;
        if (_player.PrimaryAudioInputNode.Position.TotalMilliseconds < LyricBox.Context.CurrentLyricTime)
        {
            LyricBox.Context.CurrentLyricTime = (long)_player.PrimaryAudioInputNode.Position.TotalMilliseconds;
            LyricBox.ReflowTime(0);
        }
        else
        {
            LyricBox.Context.CurrentLyricTime = (long)_player.PrimaryAudioInputNode.Position.TotalMilliseconds;
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
        if (_state.NowPlayingItem == null) return;
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

    private async void LyricView_OnRequestSeek(RenderingLyricLine line)
    {
        await _control.SeekAsync(TimeSpan.FromMilliseconds(line.StartTime));
    }

    private void LoadLyrics()
    {
        LyricBox.SetLyricLines(LrcConverter.Convert(ExpandedPlayer.ConvertToALRC(_lyricService.CurrentLyricInfo.Lyrics)));
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
        LyricBox.Redesign((float)e.NewSize.Width, (float)e.NewSize.Height, LyricView.Dpi);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        UnregisterEvents();
    }
}