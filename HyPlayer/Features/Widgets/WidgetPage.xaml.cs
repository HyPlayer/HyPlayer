using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

namespace HyPlayer.Features.Widgets;

public sealed partial class WidgetPage : Page
{
    private XboxGameBarWidget _widget;
    private XboxGameBarHotkeyWatcher _hotkeyWatcher;
    private readonly GameBarSettings _gameBarSettings;
    private readonly LyricRenderView LyricBox = new();

    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly ILyricService _lyricService = Ioc.Default.GetRequiredService<ILyricService>();
    private bool _eventsRegistered;
    private bool _windowClosedRegistered;
    private bool _lyricEventsRegistered;
    private bool _positionChangedBySeeking;
    private readonly WeakEventListener<WidgetPage, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly WeakEventListener<WidgetPage, object?, SeekRequestedEventArgs> _seekRequestedListener;


    public WidgetPage()
    {
        this.InitializeComponent();
        _stateChangedListener = new WeakEventListener<WidgetPage, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _seekRequestedListener = new WeakEventListener<WidgetPage, object?, SeekRequestedEventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.HyPlayList_OnManualSeek(),
            OnDetachAction = weakEventListener => { _control.SeekRequested -= weakEventListener.OnEvent; }
        };
        _gameBarSettings = new GameBarSettings(Dispatcher);
        Instance = this;
        Window.Current.Closed += WidgetPage_Closed;
        _windowClosedRegistered = true;
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
        if (!HasCurrentPlayItem()) return;
        Initialize();
    }

    private async void OnSettingsChecked(XboxGameBarWidget sender, object args)
    {
        await sender.ActivateSettingsAsync();
    }

    private void FindLyricButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCurrentPlayItem()) return;
        Initialize();
    }

    public void Initialize()
    {
        if (_eventsRegistered)
        {
            LoadLyrics();
            return;
        }

        _hotkeyWatcher = XboxGameBarHotkeyWatcher.CreateWatcher(_widget, (List<VirtualKey>)[VirtualKey.Control, VirtualKey.LeftMenu, VirtualKey.A]);//全局热键
        _hotkeyWatcher.Start();
        _widget.CloseRequested += Widget_CloseRequested;
        _widget.SettingsClicked += OnSettingsChecked;
        _widget.WindowBoundsChanged += OnResized;
        _widget.RequestedThemeChanged += RequestedThemeChanged;
        _hotkeyWatcher.HotkeySetStateChanged += OnHotkeySetStateChanged;
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _control.SeekRequested += _seekRequestedListener.OnEvent;
        _eventsRegistered = true;
        TipContent.Visibility = Visibility.Collapsed;
        LyricBox.Context.Debug = _setting.LyricRendererDebugMode;
        PlayStateIcon.Glyph = _state.IsPlaying ? "\uF8AE" : "\uF5B0";
        UpdateLyricViewSettings();
        LoadLyrics();
    }

    private void HyPlayList_OnPlaybackStatusChanged()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            PlayStateIcon.Glyph = _state.IsPlaying ? "\uF8AE" : "\uF5B0";
            LyricBox.Context.IsPlaying = _state.IsPlaying;
        });
    }

    private void RequestedThemeChanged(XboxGameBarWidget sender, object args)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            RequestedTheme = _widget.RequestedTheme;
            LyricBox.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color, Colors.Black);
        });
    }

    private void Widget_CloseRequested(XboxGameBarWidget sender, XboxGameBarWidgetCloseRequestedEventArgs args)
    {
        UnregisterEvents();
    }
    private void UnregisterEvents()
    {
        if (_eventsRegistered)
        {
            _hotkeyWatcher?.Stop();
            if (_widget is not null)
            {
                _widget.CloseRequested -= Widget_CloseRequested;
                _widget.SettingsClicked -= OnSettingsChecked;
                _widget.WindowBoundsChanged -= OnResized;
                _widget.RequestedThemeChanged -= RequestedThemeChanged;
            }
            if (_hotkeyWatcher is not null)
                _hotkeyWatcher.HotkeySetStateChanged -= OnHotkeySetStateChanged;
            _stateChangedListener.Detach();
            _seekRequestedListener.Detach();
            _eventsRegistered = false;
        }

        if (_widget is not null)
            Ioc.Default.GetRequiredService<IGameBarWidgetService>().ClearReference(_widget);
        if (_windowClosedRegistered)
        {
            Window.Current.Closed -= WidgetPage_Closed;
            _windowClosedRegistered = false;
        }
        if (ReferenceEquals(Instance, this))
            Instance = null;
        DetachLyricEvents();
        LyricBox.Clear();
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, LyricView.RemoveFromVisualTree);
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.NowPlayingSnapshot):
                HyPlayList_OnPlayItemChange(_state.NowPlayingProviderItem, _state.NowPlayingSnapshot);
                break;
            case nameof(PlaybackStateService.Position):
                HyPlayList_OnPlayPositionChange(_state.Position);
                break;
            case nameof(PlaybackStateService.IsPlaying):
                HyPlayList_OnPlaybackStatusChanged();
                break;
            case nameof(PlaybackStateService.LyricInfo):
                LoadLyrics();
                break;
        }
    }
    private void HyPlayList_OnPlayPositionChange(TimeSpan position)
    {
        var durationMs = _state.NowPlayingSnapshot?.Duration ?? _state.NowPlayingProviderItem?.Duration ?? 0;
        if (durationMs <= 0) return;

        var progress = position.TotalMilliseconds / durationMs * 100;
        var text = $"{position:mm\\:ss}/{TimeSpan.FromMilliseconds(durationMs):mm\\:ss}";
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

    private void HyPlayList_OnPlayItemChange(SingleSongBase? providerItem, PlaybackCurrentItemSnapshot? snapshot)
    {
        providerItem ??= _state.NowPlayingProviderItem;
        snapshot ??= _state.NowPlayingSnapshot;
        var playItemName = providerItem?.Name ?? snapshot?.Name ?? string.Empty;
        var artistName = providerItem?.CreatorList is { Count: > 0 } creators
            ? string.Join(" / ", creators)
            : snapshot?.ArtistText ?? string.Empty;
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
        await _control.MovePreviousAndPlayAsync();
    }

    private async void MoveNextButton_Click(object sender, RoutedEventArgs e)
    {
        await _control.MoveNextAndPlayAsync(true);
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

    private void HyPlayList_OnManualSeek()
    {
        _positionChangedBySeeking = true;
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
        AttachLyricEvents();
        LyricBox.Context.LyricPaddingTopRatio = _setting.lyricPaddingTopRatio / 100f;
        LyricBox.Context.Debug = _setting.LyricRendererDebugMode;
        LyricBox.Context.Effects.Blur = _setting.lyricRenderBlur;
        LyricBox.Context.Effects.Fade = _setting.lyricRenderFade;
        LyricBox.Context.Effects.FadingRatio = _setting.lyricFadingRatio;
        LyricBox.Context.Effects.CacheRenderTarget = _setting.lyricCacheRenderTarget;
        LyricBox.Context.LineRollingEaseCalculator = _setting.LineRollingCalculator switch
        {
            RollingCalculator.SinRollingCalculator => new SinRollingCalculator(),
            RollingCalculator.LyricifyRollingCalculator => new LyricifyRollingCalculator(),
            RollingCalculator.SyncRollingCalculator => new SyncRollingCalculator(),
            RollingCalculator.CircleEaseRollingCalculator => new CircleEaseRollingCalculator(),
            _ => new ElasticEaseRollingCalculator()
        };
        LyricBox.Context.Effects.Transform3D = _setting.lyricRenderTransform3D;
        LyricBox.Context.Effects.ScaleWhenFocusing = _setting.lyricRenderScaleWhenFocusing;
        LyricBox.Context.Effects.FocusHighlighting = _setting.lyricRenderFocusHighlighting;
        LyricBox.Context.Effects.TransliterationScanning = _setting.lyricRenderTransliterationScanning;
        LyricBox.Context.Effects.SimpleLineScanning = _setting.lyricRenderSimpleLineScanning;
        LyricBox.Context.Effects.ScanStyle = _setting.lyricRenderScanStyle;
        LyricBox.Context.PreferTypography.Font = _gameBarSettings.LyricFontFamily;
        LyricBox.Context.LineSpacing = _gameBarSettings.LyricLineSpacing;
        LyricBox.EnableTranslation = _gameBarSettings.EnableTranslation;
        LyricBox.EnableTransliteration = _gameBarSettings.EnableTransliteration;
        LyricBox.Context.CurrentLyricTime = 0;
        LyricBox.Context.LyricWidthRatio = 1;
        LyricBox.ChangeRenderColor(GetIdleBrush().Color, GetAccentBrush().Color, Colors.Black);
        UpdateLyricSize();
    }

    private void AttachLyricEvents()
    {
        if (_lyricEventsRegistered)
            return;

        LyricBox.OnBeforeRender += LyricBox_OnBeforeRender;
        LyricBox.OnLyricLineClicked += LyricView_OnRequestSeek;
        _lyricEventsRegistered = true;
    }

    private void DetachLyricEvents()
    {
        if (!_lyricEventsRegistered)
            return;

        LyricBox.OnBeforeRender -= LyricBox_OnBeforeRender;
        LyricBox.OnLyricLineClicked -= LyricView_OnRequestSeek;
        _lyricEventsRegistered = false;
    }

    private void LyricBox_OnBeforeRender(LyricRenderView view)
    {
        if (_player.PrimaryAudioInputNode == null) return;
        LyricBox.Context.IsPlaying = _state.IsPlaying;
        long position = 0;
        if (_player.PrimaryAudioInputNode != null) position = (long)_player.PrimaryAudioInputNode.Position.TotalMilliseconds;
        if (position < LyricBox.Context.CurrentLyricTime)
        {
            LyricBox.Context.CurrentLyricTime = position;
            LyricBox.ReflowTime(0);
        }
        else
        {
            LyricBox.Context.CurrentLyricTime = position;
        }
        LyricBox.Context.IsSeek = _positionChangedBySeeking;
        _positionChangedBySeeking = false;
    }

    private void UpdateLyricSize()
    {
        if (!HasCurrentPlayItem()) return;
        var lyricSize = _gameBarSettings.LyricSize <= 0
            ? Math.Max(_widget.WindowBounds.Width / 20, 40)
            : _gameBarSettings.LyricSize;
        var translationSize = (_gameBarSettings.TranslationSize > 0) ? _gameBarSettings.TranslationSize : lyricSize / 1.8;
        var romajiSize = (_gameBarSettings.RomajiSize > 0) ? _gameBarSettings.RomajiSize : lyricSize / 2;

        LyricBox.ChangeRenderFontSize((float)lyricSize, (float)translationSize, (float)romajiSize);
        LyricBox.ChangeAlignment(_gameBarSettings.LyricAlignment switch
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
        var lyricInfo = _lyricService.CurrentLyricInfo;
        var durationMs = _player.PrimaryAudioInputNode?.Duration.TotalMilliseconds ?? 0;
        if (_state.LyricInfo.PureLyricInfo is not HyALRCLyricInfo alrcLyricInfo)
        {
            LyricBox.SetLyricLines(LrcConverter.Convert(
                Utils.ConvertToALRC(_state.LyricInfo.Lyrics, _player.PrimaryAudioInputNode?.Duration.TotalMilliseconds ?? 0),
                _state.LyricInfo.LyricMetadata,
                _state.LyricInfo.SongMetadata,
                _setting.OptimizeLyric));
        }
        else
        {
            LyricBox.SetLyricLines(LrcConverter.Convert(
                alrcLyricInfo.ALRC,
                alrcLyricInfo.LyricMetadata,
                alrcLyricInfo.SongMetadata,
                _setting.OptimizeLyric));
        }
        LyricBox.ReflowTime(0);
        _ = Dispatcher.RunAsync(
           CoreDispatcherPriority.Normal,
           () =>
           {
               LyricBox.Redesign((float)LyricView.ActualWidth, (float)LyricView.ActualHeight, LyricView.Dpi);
        });
    }

    private bool HasCurrentPlayItem()
    {
        return _state.NowPlayingProviderItem is not null
               || _state.NowPlayingSnapshot is not null
               || _playCore.CurrentSong is not null;
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