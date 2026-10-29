#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
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
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Animations;
using CommunityToolkit.WinUI.Helpers;
using CommunityToolkit.WinUI.Media;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.DynamicCover;
using HyPlayer.Features.Playback.Services;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.Platform.Imaging;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.UI.Dialogs;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using LrcConverter = HyPlayer.Domain.Lyrics.LrcConverter;
using UISettings = Windows.UI.ViewManagement.UISettings;
using ColorHelper = HyPlayer.Platform.Imaging.ColorHelper;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Shell.ExpandedPlayer;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ExpandedPlayer : Page
{
    private const float LyricBoxRightPadding = 32;
    private const double ResponsiveBreakpointWidth = 800;
    private const double PlaybarReservedHeight = 80;
    private const double CollapseButtonRevealHeight = 120;
    private const double LyricFadeMilliseconds = 300;
    private const float MaxBackgroundDim = 0.38f;
    private const double LeftPanelShiftMilliseconds = 500;
    private const double LyricCanvasIdlePauseMs = 1000;
    private const double IdleOverlayRefreshMs = 250;
    private const float LikeAppleCanvasOverscan = 128;
    private const int CoverRefreshPendingSeconds = 5;

    public static readonly DependencyProperty NowPlaybackSpeedProperty = DependencyProperty.Register(
        "NowPlaybackSpeed", typeof(string), typeof(ExpandedPlayer),
        new PropertyMetadata("x1"));

    private readonly DebugOverlayLayer _debugOverlayLayer;
    private readonly IsolationBackgroundLayer _isolationBackgroundLayer;
    private readonly LikeAppleBackgroundLayer _likeAppleBackgroundLayer;
    private readonly ExpandedCanvasState _canvasState = new();
    private readonly WeakEventListener<ExpandedPlayer, object?, EventArgs> _enteredForegroundListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, HandledEventArgs> _escapeRequestedListener;
    private readonly ExpandedCanvasHost _expandedCanvasHost = new();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly HttpClient _httpClient = Ioc.Default.GetRequiredService<HttpClient>();
    private readonly IAppLifecycleStateService _lifecycle = Ioc.Default.GetRequiredService<IAppLifecycleStateService>();
    private readonly LyricRenderView _lyricBox = new();

    private readonly ILyricRenderSettingsService _lyricRenderSettings =
        Ioc.Default.GetRequiredService<ILyricRenderSettingsService>();

    private readonly LyricsLayer _lyricsLayer;
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

    private readonly IPlayBarAutoHideService _playBarAutoHide =
        Ioc.Default.GetRequiredService<IPlayBarAutoHideService>();

    private readonly WeakEventListener<ExpandedPlayer, object?, PlayBarVisibilityChangedEventArgs>
        _playBarVisibilityListener;

    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly WeakEventListener<ExpandedPlayer, object?, EventArgs> _secondTickListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, SeekRequestedEventArgs> _seekRequestedListener;
    private readonly ExpandedPlayerShareSaveController? _shareSave;
    private readonly ExpandedPlayerDynamicCover _dynamicCover;
    private readonly SpectrumLayer _spectrumLayer;
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _lyricSettingsChangedListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _uiSettingsChangedListener;

    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs>
        _playbackSettingsChangedListener;

    private readonly IPlaybackSurfaceCoordinator _surfaceCoordinator =
        Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();

    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _surfaceStoreChangedListener;
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();


    private readonly Storyboard _bpmAniStoryboard = new();

    private readonly Storyboard _luminousColorsRotateStoryBoard = new();
    private List<Color> _albumColors = [];
    private List<Vector3> _albumColorVectors = [];
    private volatile bool _isCleanedUp;
    private bool _appleControlsActive;
    private bool _canvasResourcesReleased;
    private bool _chromeStateApplied;
    private bool _collapseButtonShown;
    private bool _isImmersive;
    private bool _lyricsSuppressed;
    private bool _restoreLyricOnly;
    private bool _lyricAvailabilityApplied;
    private int _lyricPanelVersion;
    private float _lyricFadeFrom = 1;
    private float _lyricFadeTo = 1;
    private long _lyricFadeStart;
    private long _lyricActivityTimestamp = Stopwatch.GetTimestamp();
    private int _lyricCanvasPausePending;
    private DispatcherTimer? _idleOverlayTimer;
    private bool _appliedFixedTimeStep;
    private long _appliedTargetElapsedTicks;
    private Storyboard? _leftPanelShiftStoryboard;
    private Storyboard? _lyricPreviewHintStoryboard;
    private bool _isManualChangeMode;
    private bool _isRealClick;
    private string? _lastSongTitle;
    private readonly CoverColorRefreshGate _coverColorGate = new(Stopwatch.Frequency * CoverRefreshPendingSeconds);
    private int _coverSwapVersion;
    private bool _hasDisplayedCover;
    private double _coverLuminance;
    private Windows.UI.Composition.SpriteVisual? _dimBlur;
    private Windows.UI.Composition.SpriteVisual? _dimTint;
    private int _lastHeight;
    private int _lastRedesignHeight;
    private int _lastWidth;
    private bool _lyricHasBeenLoaded;
    private int _lyricApplyQueued;
    private readonly List<SongLyric> _lyricList = [];
    private bool _needsRedesign = true;
    private bool _windowSizeSubscribed;
    private int _nowHeight;
    private int _nowWidth;
    private bool _positionChangedBySeeking;
    private ExpandedWindowMode _windowMode;
    private Color? _albumMainColor;
    private AppWindow? _expandedPlayerWindow;

    private double _lastChangedLyricWidth;
    private DoubleAnimation _luminousColorsRotateAnimation = new();

    public ExpandedPlayer()
    {
        InitializeComponent();
        _lyricBox.EnableRenderThreadUpdates();
        ViewModel = Ioc.Default.GetRequiredService<ExpandedPlayerViewModel>();
        _dynamicCover = new ExpandedPlayerDynamicCover(
            ImageAlbum,
            Ioc.Default.GetRequiredService<IDynamicCoverService>(),
            _playbackSettings,
            _taskRunner,
            ReconcileAlbumCover);
        _dynamicCover.Load(_state.NowPlayingProviderItem);
        _lyricBox.SetRenderSettings(_lyricRenderSettings.Current);
        _lyricRenderSettings.SettingsChanged += OnLyricEffectSettingsChanged;
        _canvasState.LyricBox = _lyricBox;
        SyncCanvasState();
        _canvasState.BackgroundType = _uiSettings.ExpandedPlayerBackgroundType;
        _canvasState.ShowSpectrum = _playbackSettings.ShowSpectrum;
        _canvasState.ShowDebugOverlay = _uiSettings.ExpandedPlayerDebugMode;
        _isolationBackgroundLayer = new IsolationBackgroundLayer(_canvasState, _lyricSettings);
        _likeAppleBackgroundLayer = new LikeAppleBackgroundLayer(_canvasState, _player);
        _spectrumLayer = new SpectrumLayer(_canvasState, _player);
        _lyricsLayer = new LyricsLayer(_canvasState);
        _debugOverlayLayer = new DebugOverlayLayer(_canvasState);
        DataContext = ViewModel;
        _secondTickListener = new WeakEventListener<ExpandedPlayer, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.HyPlayList_OnTimerTicked(),
            OnDetachAction = weakEventListener => { _globalTimer.SecondTick -= weakEventListener.OnEvent; }
        };
        _globalTimer.SecondTick += _secondTickListener.OnEvent;
        _enteredForegroundListener = new WeakEventListener<ExpandedPlayer, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.OnEnteringForeground(),
            OnDetachAction = weakEventListener => { _lifecycle.EnteredForeground -= weakEventListener.OnEvent; }
        };
        _lifecycle.EnteredForeground += _enteredForegroundListener.OnEvent;
        _playBarVisibilityListener =
            new WeakEventListener<ExpandedPlayer, object?, PlayBarVisibilityChangedEventArgs>(this)
            {
                OnEventAction = static (instance, _, args) => instance.OnPlaybarVisibilityChanged(args.IsActivated),
                OnDetachAction = weakEventListener =>
                {
                    _playBarAutoHide.VisibilityChanged -= weakEventListener.OnEvent;
                }
            };
        _playBarAutoHide.VisibilityChanged += _playBarVisibilityListener.OnEvent;
        _stateChangedListener = new WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _surfaceStoreChangedListener = new WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnSurfaceStorePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _surfaceStore.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _surfaceStore.PropertyChanged += _surfaceStoreChangedListener.OnEvent;
        _escapeRequestedListener = new WeakEventListener<ExpandedPlayer, object?, HandledEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnEscapeRequested(args),
            OnDetachAction = weakEventListener => { _surfaceStore.EscapeRequested -= weakEventListener.OnEvent; }
        };
        _surfaceStore.EscapeRequested += _escapeRequestedListener.OnEvent;
        _seekRequestedListener = new WeakEventListener<ExpandedPlayer, object?, SeekRequestedEventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.HyPlayList_OnManualSeek(),
            OnDetachAction = weakEventListener => { _control.SeekRequested -= weakEventListener.OnEvent; }
        };
        _control.SeekRequested += _seekRequestedListener.OnEvent;
        var wakeHandler = new PointerEventHandler((_, _) => WakeLyricCanvas());
        LuminousBackground.AddHandler(PointerMovedEvent, wakeHandler, true);
        LuminousBackground.AddHandler(PointerWheelChangedEvent, wakeHandler, true);
        LuminousBackground.AddHandler(PointerPressedEvent, wakeHandler, true);
        LuminousBackground.AddHandler(PointerReleasedEvent, wakeHandler, true);
        LuminousBackground.AddHandler(PointerExitedEvent, wakeHandler, true);
        PageContainer.AddHandler(PointerMovedEvent, new PointerEventHandler(PageContainer_PointerMoved), true);
        PageContainer.AddHandler(PointerExitedEvent, new PointerEventHandler(PageContainer_PointerExited), true);
        _lyricBox.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        _lyricBox.OnBeforeRender += LyricBox_OnBeforeRender;
        _lyricBox.ScrollPreviewChanged += LyricBox_ScrollPreviewChanged;
        _lyricBox.OnChanged += WakeLyricCanvas;
        _lyricBox.OnLyricLineClicked += LyricBoxOnOnRequestSeek;
        _lyricBox.Context.LyricWidthRatio = _lyricSettings.LyricRenderWidthRatio / 100f;
        _lyricBox.Context.LyricPaddingTopRatio = _lyricSettings.LyricPaddingTopRatio / 100f;
        _lyricBox.Context.CurrentLyricTime = 0;
        _lyricBox.Context.Debug = _lyricSettings.LyricRendererDebugMode;
        _lyricBox.Context.Effects.CacheRenderTarget = _lyricSettings.LyricCacheRenderTarget;
        _lyricBox.Context.LineRollingEaseCalculator = _lyricSettings.LineRollingCalculator switch
        {
            RollingCalculator.SinRollingCalculator => new SinRollingCalculator(),
            RollingCalculator.LyricifyRollingCalculator => new LyricifyRollingCalculator(),
            RollingCalculator.SyncRollingCalculator => new SyncRollingCalculator(),
            RollingCalculator.CircleEaseRollingCalculator => new CircleEaseRollingCalculator(),
            RollingCalculator.PerLineSpringRollingCalculator => new PerLineSpringRollingCalculator(),
            _ => new ElasticEaseRollingCalculator()
        };
        _lyricBox.Context.Effects.TransliterationScanning = _lyricSettings.LyricRenderTransliterationScanning;
        _lyricBox.Context.Effects.SimpleLineScanning = _lyricSettings.LyricRenderSimpleLineScanning;
        _lyricBox.Context.Effects.ScanStyle = _lyricSettings.LyricRenderScanStyle;
        _lyricBox.Context.PreferTypography.Font = _lyricSettings.LyricFontFamily;
        _lyricBox.Context.LineSpacing = LyricLineSpacingRule.Resolve(_lyricSettings.LyricLineSpacing, (float)LyricShowSize);
        _lyricSettingsChangedListener = new WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnLyricSettingsPropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _lyricSettings.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _lyricSettings.PropertyChanged += _lyricSettingsChangedListener.OnEvent;
        _uiSettingsChangedListener = new WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.SyncCanvasSettings(),
            OnDetachAction = weakEventListener => { _uiSettings.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _uiSettings.PropertyChanged += _uiSettingsChangedListener.OnEvent;
        _playbackSettingsChangedListener =
            new WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs>(this)
            {
                OnEventAction = static (instance, _, _) => instance.SyncCanvasSettings(),
                OnDetachAction = weakEventListener =>
                {
                    _playbackSettings.PropertyChanged -= weakEventListener.OnEvent;
                }
            };
        _playbackSettings.PropertyChanged += _playbackSettingsChangedListener.OnEvent;

        _expandedCanvasHost.AddLayer(_isolationBackgroundLayer);
        _expandedCanvasHost.AddLayer(_likeAppleBackgroundLayer);
        _expandedCanvasHost.AddLayer(_spectrumLayer);
        _expandedCanvasHost.AddLayer(_lyricsLayer);
        _expandedCanvasHost.AddLayer(_debugOverlayLayer);

        // ── Stage 8: Share/save controller ────────────────────────────
        _shareSave = new ExpandedPlayerShareSaveController(
            _state, _httpClient, _playCore, _notification,
            () => TextBlockSongTitle.Text);
    }

    private InMemoryRandomAccessStream? EffectiveCoverStream => _dynamicCover.Poster ?? _state.CoverStream;

    // Services accessed via ViewModel; shortcuts for code-behind convenience
    private PlayCoreBase _playCore => ViewModel.PlayCore;
    private IPlaybackControlService _control => ViewModel.Control;
    private PlaybackStateService _state => ViewModel.State;
    private ILyricService _lyricService => ViewModel.LyricService;
    private PlaybackSettings _playbackSettings => ViewModel.PlaybackSettings;
    private HyPlayer.Domain.Settings.UISettings _uiSettings => ViewModel.UISettings;
    private LyricSettings _lyricSettings => ViewModel.LyricSettings;

    public double LyricShowSize { get; set; }
    public double LyricWidth { get; set; }

    public string NowPlaybackSpeed
    {
        get => (string)GetValue(NowPlaybackSpeedProperty);
        set => SetValue(NowPlaybackSpeedProperty, value);
    }

    public ExpandedPlayerViewModel ViewModel { get; }

    private void SyncCanvasSettings()
    {
        if (_isCleanedUp) return;
        var showSpectrum = _playbackSettings.ShowSpectrum;
        _canvasState.BackgroundType = _uiSettings.ExpandedPlayerBackgroundType;
        _canvasState.ShowSpectrum = showSpectrum;
        _canvasState.ShowDebugOverlay = _uiSettings.ExpandedPlayerDebugMode;
        _player.EnableFFTProcessing = _playbackSettings.EnableFFT || showSpectrum ||
            _canvasState.BackgroundType == BackgroundType.LikeApple;
        if (Dispatcher.HasThreadAccess) ApplyCanvasOverscan();
        else RunOnUIThread(ApplyCanvasOverscan);
        WakeLyricCanvas();
    }

    private void ApplyCanvasOverscan()
    {
        if (_isCleanedUp || LuminousBackground is null) return;
        var overscan = _canvasState.BackgroundType == BackgroundType.LikeApple ? LikeAppleCanvasOverscan : 0;
        LuminousBackground.Margin = new Thickness(0, 0, -overscan, -overscan);
    }

    private void SyncCanvasState()
    {
        _canvasState.IsPlaying = _state.IsPlaying;
        _canvasState.IsTransitioning = _state.IsTransitioning;
        _canvasState.WindowMode = _windowMode;
        _canvasState.AlbumColorVectors = _albumColorVectors;
        _canvasState.LyricOpacity = GetLyricOpacity();
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.IsPlaying):
                WakeLyricCanvas();
                if (_state.IsPlaying) HyPlayList_OnPlay();
                else HyPlayList_OnPause();
                break;
            case nameof(PlaybackStateService.NowPlayingProviderItem):
            case nameof(PlaybackStateService.NowPlayingSnapshot):
                _canvasState.TrackChangeTimestamp = Stopwatch.GetTimestamp();
                WakeLyricCanvas();
                OnSongChange();
                _dynamicCover.Load(_state.NowPlayingProviderItem);
                ReconcileAlbumCover();
                break;
            case nameof(PlaybackStateService.IsTransitioning):
                if (!_state.IsTransitioning) ReconcileAlbumCover();
                break;
            case nameof(PlaybackStateService.CoverStream):
                WakeLyricCanvas();
                _taskRunner.Forget(RefreshAlbumCoverAsync(), "refresh expanded player cover");
                break;
            case nameof(PlaybackStateService.LyricInfo):
            case nameof(PlaybackStateService.IsLyricPending):
                WakeLyricCanvas();
                HyPlayList_OnLyricLoaded();
                break;
        }
    }

    private void OnLyricSettingsPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(LyricSettings.LyricLineSpacing)) ApplyLyricLineSpacing();
        if (propertyName is nameof(LyricSettings.IsolationFullThrottle) or nameof(LyricSettings.IsolationFPS))
            ConfigureFramePacing();
    }

    private void ApplyLyricLineSpacing()
    {
        _lyricBox.ChangeLineSpacing(
            LyricLineSpacingRule.Resolve(_lyricSettings.LyricLineSpacing, (float)LyricShowSize));
    }

    private void OnSurfaceStorePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackSurfaceStore.TransitionRequestId):
                WakeLyricCanvas();
                if (_surfaceStore.RequestedTransition == ExpandedPlayerTransition.Expand)
                    StartExpandAnimation();
                break;
            case nameof(PlaybackSurfaceStore.SurfaceMode):
                WakeLyricCanvas();
                if (_surfaceStore.IsExpanded) ApplyChromeState();
                else ExitImmersive();
                break;
            case nameof(PlaybackSurfaceStore.ExpandedFrameOffsetY):
                // MainPage observes the same store value and moves the frame. ExpandedPlayer keeps the gesture local state here.
                break;
        }
    }

    private void HyPlayList_OnManualSeek()
    {
        WakeLyricCanvas();
        _positionChangedBySeeking = true;
    }

    private void LyricBoxOnOnRequestSeek(RenderingLyricLine line)
    {
        if (line is ActionLyricLine actionLyricLine)
        {
            var action = actionLyricLine.ActionUri;
            if (action.StartsWith("hyplayer://"))
            {
                if (AppRoute.TryParseExternalResource(action[11..], out var route))
                    _taskRunner.Forget(_navigator.NavigateAsync(route), "navigate from lyric action");
                _surfaceCoordinator.Collapse();
            }
            else
            {
                _taskRunner.Forget(Launcher.LaunchUriAsync(new Uri(action)).AsTask(), "launch lyric action uri");
            }
        }
        else
        {
            _taskRunner.Forget(_control.SeekAsync(TimeSpan.FromMilliseconds(line.StartTime)), "seek from lyric click");
        }
    }

    private readonly LyricClock _lyricClock = new();
    private readonly LyricTrackGuard _lyricTrackGuard = new();
    private SingleSongBase? _lyricEventSong;

    private void LyricBox_OnBeforeRender(LyricRenderView view)
    {
        view.Context.IsPlaying = _state.IsPlaying;
        if (_lyricTrackGuard.Holds(_state.NowPlayingProviderItem, Stopwatch.GetTimestamp()))
            return;

        var primaryAudioInputNode = _player.PrimaryAudioInputNode;
        if (primaryAudioInputNode == null)
            return;

        var seek = _positionChangedBySeeking;
        var precisePosition = Math.Max(0, _lyricClock.NextPrecise(primaryAudioInputNode,
            _player.GetPosition(primaryAudioInputNode).TotalMilliseconds, _player.GlobalPlaybackStatus == PlaybackStatus.Playing,
            _player.GetSpeed(primaryAudioInputNode), seek));
        var positionMilliseconds = (long)precisePosition;
        if (positionMilliseconds < view.Context.CurrentLyricTime)
        {
            view.Context.PreciseLyricTime = precisePosition;
            _lyricBox.ReflowTime(0);
        }
        else
        {
            view.Context.PreciseLyricTime = precisePosition;
        }

        view.Context.IsSeek = seek;
        _positionChangedBySeeking = false;
    }

    public void SingleViewModeToggle()
    {
        if (_windowMode == ExpandedWindowMode.Both) return;
        if (_lyricsSuppressed && _windowMode == ExpandedWindowMode.CoverOnly) return;
        _windowMode = _windowMode == ExpandedWindowMode.LyricOnly
            ? ExpandedWindowMode.CoverOnly
            : ExpandedWindowMode.LyricOnly;
        ChangeWindowMode();
    }

    private void HyPlayList_OnPlay()
    {
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (_uiSettings.AlbumRotate)
                //网易云音乐圆形唱片
                RotateAnimationSet.StartAsync();
            if (_uiSettings.ExpandAlbumBreath) ImageAlbumAni.Begin();
            if (_luminousColorsRotateStoryBoard.Children.Count > 0) _luminousColorsRotateStoryBoard.Resume();
        });
    }

    private void HyPlayList_OnPause()
    {
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (_uiSettings.AlbumRotate)
                RotateAnimationSet.Stop();
            if (_uiSettings.ExpandAlbumBreath) ImageAlbumAni.Pause();

            if (_bpmAniStoryboard.Children.Count > 0) _bpmAniStoryboard.Pause();

            if (_luminousColorsRotateStoryBoard.Children.Count > 0) _luminousColorsRotateStoryBoard.Pause();
        });
    }

    private void HyPlayList_OnTimerTicked()
    {
        if (_lifecycle.IsInBackground) return;
        if (_needsRedesign)
        {
            _needsRedesign = false;
            WakeLyricCanvas();
            RunOnUIThread(Redesign);
        }
    }

    private void WakeLyricCanvas()
    {
        Volatile.Write(ref _lyricActivityTimestamp, Stopwatch.GetTimestamp());
        if (Dispatcher.HasThreadAccess)
            ResumeLyricCanvas();
        else
            RunOnUIThread(ResumeLyricCanvas);
    }

    private void ResumeLyricCanvas()
    {
        if (LuminousBackground is not { Paused: true }) return;
        _canvasState.FramePacer.Suspend();
        LuminousBackground.Paused = false;
        _canvasState.IsCanvasIdle = false;
        _idleOverlayTimer?.Stop();
    }

    private void RefreshIdleOverlay()
    {
        if (!_isCleanedUp && _canvasState.ShowDebugOverlay && LuminousBackground is { Paused: true })
            LuminousBackground.Invalidate();
        else
            _idleOverlayTimer?.Stop();
    }

    private bool IsLyricCanvasIdle()
    {
        if (_lifecycle.IsInBackground) return true;
        return !_state.IsPlaying && _player.GlobalPlaybackStatus != PlaybackStatus.Playing &&
               Stopwatch.GetElapsedTime(Volatile.Read(ref _lyricActivityTimestamp)).TotalMilliseconds >=
               LyricCanvasIdlePauseMs;
    }

    private void TryPauseIdleLyricCanvas()
    {
        if (!IsLyricCanvasIdle() || !_lifecycle.IsInBackground && _expandedCanvasHost.NeedsFrames) return;
        if (Interlocked.Exchange(ref _lyricCanvasPausePending, 1) == 1) return;
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
        {
            _lyricCanvasPausePending = 0;
            if (_isCleanedUp || LuminousBackground is null || !IsLyricCanvasIdle()) return;
            _canvasState.IsCanvasIdle = true;
            LuminousBackground.Paused = true;
            if (!_canvasState.ShowDebugOverlay) return;
            if (_idleOverlayTimer is null)
            {
                _idleOverlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IdleOverlayRefreshMs) };
                _idleOverlayTimer.Tick += (_, _) => RefreshIdleOverlay();
            }

            _idleOverlayTimer.Start();
        });
    }

    private void HyPlayList_OnLyricLoaded()
    {
        Volatile.Write(ref _lyricEventSong, _state.NowPlayingProviderItem);
        if (Interlocked.Exchange(ref _lyricApplyQueued, 1) == 1) return;
        LoadLyricsBox();
        _needsRedesign = true;
    }

    private void Current_SizeChanged(object? sender, WindowSizeChangedEventArgs? e)
    {
        WakeLyricCanvas();
        _nowWidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
        _nowHeight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
        if (_lastWidth == _nowWidth && _lastHeight == _nowHeight) return;
        if (_lastWidth != _nowWidth)
        {
            LyricWidth = CalculateLyricWidth();
            LyricShowSize = _uiSettings.LyricSize <= 0
                ? Math.Max(_nowWidth / 40, 40)
                : _uiSettings.LyricSize;
        }

        _lastWidth = _nowWidth;
        _lastHeight = _nowHeight;
        UpdateAlbumSizeLimit();
        if (_isCleanedUp || !IsLoaded) return;
        ApplyResponsiveWindowMode();
        SyncLyricViewport();
    }

    private void RightPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isCleanedUp || !IsLoaded) return;
        WakeLyricCanvas();
        SyncLyricViewport();
    }

    private double CalculateLyricWidth()
    {
        var baseWidth = _windowMode == ExpandedWindowMode.Both
            ? _nowWidth * 0.5
            : _nowWidth - 30;
        return Math.Max(baseWidth - LyricBoxRightPadding, 0);
    }

    private void ChangeWindowMode()
    {
        WakeLyricCanvas();
        _isRealClick = false;
        _canvasState.WindowMode = _windowMode;

        LyricWidth = CalculateLyricWidth();

        switch (_windowMode)
        {
            case ExpandedWindowMode.Both:
                BtnToggleAlbum.IsChecked = true;
                BtnToggleLyric.IsChecked = true;
                RightPanel.Visibility = Visibility.Visible;
                LeftPanel.Visibility = Visibility.Visible;
                InfoPanelColumn.Width = new GridLength(1, GridUnitType.Star);
                LyricPanelColumn.Width = new GridLength(1, GridUnitType.Star);
                break;
            case ExpandedWindowMode.CoverOnly:
                BtnToggleAlbum.IsChecked = true;
                BtnToggleLyric.IsChecked = false;
                RightPanel.Visibility = Visibility.Collapsed;
                LeftPanel.Visibility = Visibility.Visible;
                InfoPanelColumn.Width = new GridLength(1, GridUnitType.Star);
                LyricPanelColumn.Width = new GridLength(0);
                break;
            case ExpandedWindowMode.LyricOnly:
                BtnToggleAlbum.IsChecked = false;
                BtnToggleLyric.IsChecked = true;
                LeftPanel.Visibility = Visibility.Collapsed;
                RightPanel.Visibility = Visibility.Visible;
                InfoPanelColumn.Width = new GridLength(0);
                LyricPanelColumn.Width = new GridLength(1, GridUnitType.Star);
                break;
        }

        UpdateControlStyle();
        if (_windowMode != ExpandedWindowMode.CoverOnly) StartLyricFade(1);
        _needsRedesign = true;
        _isRealClick = true;
    }

    private float GetLyricOpacity()
    {
        var start = Volatile.Read(ref _lyricFadeStart);
        if (start == 0) return _lyricFadeTo;
        var progress = (float)Math.Clamp(
            Stopwatch.GetElapsedTime(start).TotalMilliseconds / LyricFadeMilliseconds, 0, 1);
        var eased = progress * progress * (3 - 2 * progress);
        return _lyricFadeFrom + (_lyricFadeTo - _lyricFadeFrom) * eased;
    }

    private void StartLyricFade(float to, float? from = null)
    {
        var current = from ?? GetLyricOpacity();
        if (Math.Abs(current - to) < 0.001f && Math.Abs(_lyricFadeTo - to) < 0.001f) return;
        _lyricFadeFrom = current;
        _lyricFadeTo = to;
        Volatile.Write(ref _lyricFadeStart, Stopwatch.GetTimestamp());
        WakeLyricCanvas();
    }

    private int CountEffectiveLyricLines()
    {
        var info = _state.LyricInfo;
        if (info?.PureLyricInfo is HyALRCLyricInfo alrcLyricInfo)
            return alrcLyricInfo.ALRC?.Lines?.Count(line =>
                !string.IsNullOrWhiteSpace(line.RawText) ||
                (line.Words?.Any(word => !string.IsNullOrWhiteSpace(word.Word)) ?? false)) ?? 0;
        return info?.Lyrics?.Count(lyric => !string.IsNullOrWhiteSpace(lyric.LyricLine?.CurrentLyric)) ?? 0;
    }

    private void ApplyLyricAvailability()
    {
        var suppress = _lyricSettings.AutoHideLyricPanel &&
                       CountEffectiveLyricLines() <= (int)_lyricSettings.AutoHideLyricPanelLines;
        var animate = IsLoaded && _lyricAvailabilityApplied;
        _lyricAvailabilityApplied = true;
        if (suppress == _lyricsSuppressed) return;
        _lyricsSuppressed = suppress;
        var version = ++_lyricPanelVersion;
        if (suppress)
        {
            // 仅歌词时没有可显示的歌词，先切到仅封面，歌词恢复后再切回
            if (_windowMode == ExpandedWindowMode.LyricOnly)
            {
                _restoreLyricOnly = true;
                _windowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
                return;
            }

            if (_windowMode != ExpandedWindowMode.Both) return;
            if (!animate)
            {
                _windowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
                return;
            }

            _taskRunner.Forget(HideLyricPanelAsync(version), "hide lyric panel");
            return;
        }

        var restoreLyricOnly = _restoreLyricOnly;
        _restoreLyricOnly = false;
        if (_windowMode != ExpandedWindowMode.CoverOnly || _isManualChangeMode ||
            _nowWidth <= ResponsiveBreakpointWidth)
        {
            if (restoreLyricOnly && _windowMode == ExpandedWindowMode.CoverOnly)
            {
                _windowMode = ExpandedWindowMode.LyricOnly;
                ChangeWindowMode();
                return;
            }

            StartLyricFade(1);
            return;
        }

        if (!animate)
        {
            _windowMode = ExpandedWindowMode.Both;
            ChangeWindowMode();
            return;
        }

        StartLyricFade(0, 0);
        ShiftLeftPanel(() =>
        {
            _windowMode = ExpandedWindowMode.Both;
            ChangeWindowMode();
        });
    }

    private async Task HideLyricPanelAsync(int version)
    {
        StartLyricFade(0);
        await Task.Delay(TimeSpan.FromMilliseconds(LyricFadeMilliseconds));
        if (version != _lyricPanelVersion || _isCleanedUp || _windowMode != ExpandedWindowMode.Both) return;
        ShiftLeftPanel(() =>
        {
            _windowMode = ExpandedWindowMode.CoverOnly;
            ChangeWindowMode();
        });
    }

    private void ShiftLeftPanel(Action changeLayout)
    {
        var before = GetAlbumBounds(PageContainer);
        _leftPanelShiftStoryboard?.Stop();
        _leftPanelShiftStoryboard = null;
        changeLayout();
        UpdateLayout();
        var after = GetAlbumBounds(PageContainer);
        if (before is not { } from || after is not { } to || to.Width <= 0 || to.Height <= 0) return;

        var center = ImageAlbum.TransformToVisual(LeftPanel)
            .TransformPoint(new Point(ImageAlbum.ActualWidth / 2, ImageAlbum.ActualHeight / 2));
        LeftPanelTransform.CenterX = center.X;
        LeftPanelTransform.CenterY = center.Y;
        var scale = from.Width / to.Width;
        var deltaX = from.X + from.Width / 2 - (to.X + to.Width / 2);
        var deltaY = from.Y + from.Height / 2 - (to.Y + to.Height / 2);
        if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5 && Math.Abs(scale - 1) < 0.001) return;

        var storyboard = new Storyboard();
        AddShiftAnimation(storyboard, "TranslateX", deltaX, 0);
        AddShiftAnimation(storyboard, "TranslateY", deltaY, 0);
        AddShiftAnimation(storyboard, "ScaleX", scale, 1);
        AddShiftAnimation(storyboard, "ScaleY", scale, 1);
        _leftPanelShiftStoryboard = storyboard;
        storyboard.Begin();
    }

    private void AddShiftAnimation(Storyboard storyboard, string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(LeftPanelShiftMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, LeftPanelTransform);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private Rect? GetAlbumBounds(UIElement relativeTo)
    {
        if (LeftPanel.Visibility != Visibility.Visible || ImageAlbum.ActualWidth <= 0 ||
            ImageAlbum.ActualHeight <= 0) return null;
        return ImageAlbum.TransformToVisual(relativeTo)
            .TransformBounds(new Rect(0, 0, ImageAlbum.ActualWidth, ImageAlbum.ActualHeight));
    }

    private void UpdateControlStyle()
    {
        var apple = _uiSettings.ExpandedControlStyle == ExpandedControlStyle.Apple &&
                    _windowMode != ExpandedWindowMode.LyricOnly;
        if (apple && ApplePlaybackControls is null)
        {
            FindName(nameof(ApplePlaybackControls));
            ApplePlaybackControls?.ViewModel = ViewModel;
            ApplePlaybackControls?.SizeChanged += (_, _) => UpdateMoreButtonPlacement();
            MainGrid.SizeChanged += (_, _) => UpdateMoreButtonPlacement();
        }

        ApplePlaybackControls?.Visibility = apple ? Visibility.Visible : Visibility.Collapsed;
        LeftPanel.RowDefinitions[1].Height = apple ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        var changed = apple != _appleControlsActive || !_chromeStateApplied;
        _appleControlsActive = apple;
        UpdateAlbumSizeLimit();
        if (apple) SyncControlsWidth();
        if (changed) ApplyChromeState();
        UpdateMoreButtonPlacement();
    }

    private void UpdateMoreButtonPlacement()
    {
        if (_isCleanedUp) return;
        var bottom = MoreButtonBottomMargin;
        if (_appleControlsActive &&
            ApplePlaybackControls is { Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 } controls &&
            MainGrid.ActualWidth > 0)
        {
            var bounds = controls.TransformToVisual(MainGrid)
                .TransformBounds(new Rect(0, 0, controls.ActualWidth, controls.ActualHeight));
            var buttonLeft = MainGrid.ActualWidth - MoreBtn.Margin.Right - MoreBtn.Width;
            var buttonTop = MainGrid.ActualHeight - MoreButtonBottomMargin - MoreBtn.Height;
            if (bounds.Right > buttonLeft - MoreButtonClearance && bounds.Bottom > buttonTop - MoreButtonClearance)
                bottom = Math.Max(bottom, MainGrid.ActualHeight - bounds.Top + MoreButtonClearance);
        }

        if (Math.Abs(MoreBtn.Margin.Bottom - bottom) > 0.5)
            MoreBtn.Margin = new Thickness(MoreBtn.Margin.Left, MoreBtn.Margin.Top, MoreBtn.Margin.Right, bottom);
    }

    private void UpdateAlbumSizeLimit()
    {
        if (!_appleControlsActive || _nowWidth <= 0 || _nowHeight <= 0)
        {
            ImageAlbum.MaxWidth = double.PositiveInfinity;
            ImageAlbum.MaxHeight = double.PositiveInfinity;
            return;
        }

        var columnWidth = _windowMode == ExpandedWindowMode.Both ? _nowWidth / 2.0 : _nowWidth;
        var size = Math.Max(160, Math.Min(columnWidth * 0.62, _nowHeight * 0.46));
        ImageAlbum.MaxWidth = size;
        ImageAlbum.MaxHeight = size;
    }

    private void ApplyChromeState()
    {
        if (_isCleanedUp) return;
        _chromeStateApplied = true;
        var chromeVisible = !_isImmersive;
        FadeTo(MoreBtn, chromeVisible ? 1 : 0);
        FadeTo(ApplePlaybackControls, chromeVisible ? 1 : 0);
        if (!chromeVisible || !_appleControlsActive) SetCollapseButtonShown(false);
        MainGrid.Margin = new Thickness(0, 0, 0, _appleControlsActive ? 0 : PlaybarReservedHeight);
        if (_surfaceStore.IsExpanded) _surfaceStore.IsPlayBarSuppressed = _appleControlsActive || _isImmersive;
    }

    private static void FadeTo(UIElement? element, double to, double milliseconds = 250)
    {
        if (element is null) return;
        element.IsHitTestVisible = to > 0;
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void SetCollapseButtonShown(bool shown)
    {
        if (shown == _collapseButtonShown) return;
        _collapseButtonShown = shown;
        FadeTo(BtnCollapsePage, shown ? 1 : 0, 200);
    }

    private void PageContainer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_appleControlsActive || _isImmersive) return;
        SetCollapseButtonShown(e.GetCurrentPoint(PageContainer).Position.Y <= CollapseButtonRevealHeight);
    }

    private void PageContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetCollapseButtonShown(false);
    }

    private void BtnToggleImmersive_Click(object sender, RoutedEventArgs e)
    {
        if (BtnToggleImmersive.IsChecked) EnterImmersive();
        else ExitImmersive();
    }

    private void EnterImmersive()
    {
        if (_isImmersive) return;
        _isImmersive = true;
        BtnToggleImmersive.IsChecked = true;
        ApplyChromeState();
        ImmersiveHintStoryboard.Begin();
    }

    private void ExitImmersive()
    {
        if (!_isImmersive) return;
        _isImmersive = false;
        BtnToggleImmersive.IsChecked = false;
        ImmersiveHintStoryboard.Stop();
        ApplyChromeState();
    }

    private void OnEscapeRequested(HandledEventArgs args)
    {
        if (args.Handled) return;
        if (_lyricBox.IsScrollPreviewing)
        {
            _lyricBox.ResetScrolling();
            args.Handled = true;
            return;
        }

        if (!_isImmersive) return;
        ExitImmersive();
        args.Handled = true;
    }

    private void SyncControlsWidth()
    {
        if (ApplePlaybackControls is null) return;
        var width = ImageAlbum.ActualWidth;
        ApplePlaybackControls.MaxWidth =
            width > 0 ? Math.Max(width, ApplePlaybackControls.MinWidth) : double.PositiveInfinity;
    }

    private void BtnCollapsePage_Click(object sender, RoutedEventArgs e)
    {
        _surfaceCoordinator.Collapse();
    }

    private void Redesign()
    {
        // 这个函数里面放无法用XAML实现的页面布局方式
        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;
        SyncLyricViewport();
        ApplyResponsiveWindowMode();
    }

    private void SyncLyricViewport()
    {
        if (LuminousBackground is null) return;
        SyncCanvasRasterizationScale();
        ApplyLyricLineSpacing();
        var lyricOffset = RightPanel.ActualOffset;
        if (Math.Abs(_lastChangedLyricWidth - LyricWidth) > 0.001f || _lastRedesignHeight != _nowHeight ||
            Math.Abs(_canvasState.LyricRenderXOffset - lyricOffset.X) > 0.001f ||
            Math.Abs(_canvasState.LyricRenderYOffset - lyricOffset.Y) > 0.001f)
        {
            _canvasState.LyricRenderXOffset = lyricOffset.X;
            _canvasState.LyricRenderYOffset = lyricOffset.Y;
            _lastRedesignHeight = _nowHeight;
            _lyricBox.Redesign((float)LyricWidth, _nowHeight, LuminousBackground.Dpi);
            _lyricBox.ChangeRenderFontSize((float)LyricShowSize,
                _lyricSettings.TranslationSize > 0 ? _lyricSettings.TranslationSize : (float)LyricShowSize / 2,
                _lyricSettings.RomajiSize > 0 ? _lyricSettings.RomajiSize : (float)LyricShowSize / 2,
                _lyricSettings.SublineLyricSize,
                _lyricSettings.SublineTranslationSize,
                _lyricSettings.SublineRomajiSize);
            _lastChangedLyricWidth = LyricWidth;
        }
    }

    private void SyncCanvasRasterizationScale()
    {
        if (_isCleanedUp || LuminousBackground is null) return;
        _canvasState.RasterizationScale = LuminousBackground.Dpi / 96f;
    }

    private void ApplyResponsiveWindowMode()
    {
        // 响应式布局: 窗口宽度 <= ResponsiveBreakpointWidth 时仅显示封面, > ResponsiveBreakpointWidth 时恢复双栏
        if (_isManualChangeMode) return;
        if (_nowWidth <= ResponsiveBreakpointWidth && _windowMode == ExpandedWindowMode.Both)
        {
            _windowMode = ExpandedWindowMode.CoverOnly;
            ChangeWindowMode();
        }
        else if (_nowWidth > ResponsiveBreakpointWidth && _windowMode != ExpandedWindowMode.Both &&
                 !_lyricsSuppressed)
        {
            _windowMode = ExpandedWindowMode.Both;
            ChangeWindowMode();
        }
    }

    private const double MoreButtonBottomMargin = 50;
    private const double MoreButtonClearance = 8;

    private void ImageAlbum_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ImageRotateTransform.CenterX = e.NewSize.Width / 2;
        ImageRotateTransform.CenterY = e.NewSize.Height / 2;
        _dynamicCover.UpdateLayout();
        if (_appleControlsActive) SyncControlsWidth();
    }

    private void LuminousBackgroundContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        BgScale.CenterX = e.NewSize.Width / 2;
        BgScale.CenterY = e.NewSize.Height / 2;
    }

    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(this.RunOnUIThreadAsync(action), "ExpandedPlayer UI update");
    }

    public void LoadLyricsBox()
    {
        Volatile.Write(ref _lyricEventSong, _state.NowPlayingProviderItem);
        _ = this.RunOnUIThreadAsync(() =>
        {
            Volatile.Write(ref _lyricApplyQueued, 0);
            if (_isCleanedUp || _canvasResourcesReleased) return;
            var lyricOwner = Volatile.Read(ref _lyricEventSong);
            var pending = _state.IsLyricPending;
            var lyricInfo = _state.LyricInfo;
            var duration = _state.Duration > TimeSpan.Zero
                ? _state.Duration.TotalMilliseconds
                : _player.PrimaryAudioInputNode?.Duration.TotalMilliseconds ?? 0;
            if (lyricInfo.PureLyricInfo is not HyALRCLyricInfo alrcLyricInfo)
                _lyricBox.SetLyricLines(LrcConverter.Convert(
                    Utils.ConvertToALRC(lyricInfo.Lyrics, duration),
                    lyricInfo.LyricMetadata,
                    lyricInfo.SongMetadata,
                    _lyricSettings.OptimizeLyric));
            else
                _lyricBox.SetLyricLines(LrcConverter.Convert(
                    alrcLyricInfo.ALRC,
                    alrcLyricInfo.LyricMetadata,
                    alrcLyricInfo.SongMetadata,
                    _lyricSettings.OptimizeLyric));
            _lyricBox.ChangeAlignment(_uiSettings.LyricAlignment switch
            {
                LyricAlignment.Center => TextAlignment.Center,
                LyricAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            });
            _lyricBox.ReflowTime(0);
            _lyricTrackGuard.Adopt(lyricOwner);
            if (_state.NowPlayingProviderItem == null) return;
            RefreshUIColor();
            if (!pending) ApplyLyricAvailability();
            Redesign();
            _lyricHasBeenLoaded = true;
        });
    }

    internal void OnEnteringForeground()
    {
        WakeLyricCanvas();
        OnSongChange();
        _taskRunner.Forget(RefreshAlbumCoverAsync(), "refresh expanded player cover");
        if (!_lyricHasBeenLoaded) HyPlayList_OnLyricLoaded();
    }

    public void OnSongChange()
    {
        _ = this.RunOnUIThreadAsync(() =>
        {
            ViewModel.SyncFromState();
            var title = ViewModel.SongName;
            var titleChanged = _lastSongTitle is not null && _lastSongTitle != title;
            _lastSongTitle = title;
            if (titleChanged && _uiSettings.ExpandAnimation && !_isCleanedUp) FadeInSongInfo();
            NowPlaybackSpeed = FormatPlaybackSpeed(_state.PlaybackSpeed);
        });
    }

    public void RefreshUIColor()
    {
        var theme = ViewModel.DisplayedTheme;
        _lyricBox.ChangeRenderColor(theme.IdleBrush.Color, theme.AccentBrush.Color);
    }

    private void ApplyPlaybackTheme(PlaybackThemeSnapshot theme)
    {
        ViewModel.DisplayedTheme = theme;
        _canvasState.IsBrightTheme = theme.IsBright;
        _surfaceStore.Theme = theme;
    }

    public void StartExpandAnimation()
    {
        ImageAlbum.Visibility = Visibility.Visible;
        SingerHyperlinkBtn.Visibility = Visibility.Visible;
        TextBlockSongTitle.Visibility = Visibility.Visible;
        var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
        var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
        var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
        anim3?.Configuration = new DirectConnectedAnimationConfiguration();
        anim2?.Configuration = new DirectConnectedAnimationConfiguration();
        anim1?.Configuration = new DirectConnectedAnimationConfiguration();
        try
        {
            //anim3?.TryStart(TextBlockSinger);
            anim1?.TryStart(TextBlockSongTitle);
            anim2?.TryStart(ImageAlbum);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Expanded player expand animation failed: {ex}");
        }
    }

    internal ConnectedAnimationBatch PrepareCollapseAnimations(PlayBar destination)
    {
        var batch = new ConnectedAnimationBatch();
        batch.Prepare("SongTitle", TextBlockSongTitle, destination.GetCollapseAnimationTarget("SongTitle"));
        batch.Prepare("SongImg", ImageAlbum, destination.GetCollapseAnimationTarget("SongImg"));
        batch.Prepare("SongArtist", SingerHyperlinkBtn, destination.GetCollapseAnimationTarget("SongArtist"));
        batch.Prepare("SongAlbum", AlbumHyperlinkBtn, destination.GetCollapseAnimationTarget("SongAlbum"));
        return batch;
    }

    private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_expandedCanvasHost.TryHandlePointer(e))
            _lyricBox.LyricView_OnPointerWheelChanged(sender, e);
    }

    private void ToggleButtonTranslation_OnClick(object sender, RoutedEventArgs e)
    {
        _lyricBox?.EnableTranslation = ToggleButtonTranslation.IsChecked == true;
    }

    private void ToggleButtonSound_OnClick(object sender, RoutedEventArgs e)
    {
        _lyricBox?.EnableTransliteration = ToggleButtonSound.IsChecked;
    }

    private void AlbumHyperlinkBtn_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_state.NowPlayingProviderItem is SingleSongBase providerSong)
            {
                var albumId = providerSong.Album?.ActualId;
                if (!string.IsNullOrEmpty(albumId) && albumId != "0")
                    _navigation.Navigate(typeof(AlbumPage), albumId);
            }

            _surfaceCoordinator.Collapse();
        }
        catch
        {
        }
    }

    private async void TextBlockSinger_OnTapped(object sender, RoutedEventArgs tappedRoutedEventArgs)
    {
        try
        {
            if (_state.NowPlayingProviderItem is IHasCreators creatorsProvider)
            {
                var creators = await creatorsProvider.GetCreatorsAsync();
                if (creators is { Count: > 1 })
                {
                    await new ArtistSelectDialog(creators).ShowAsync();
                    return;
                }

                if (creators is { Count: 1 } && !string.IsNullOrWhiteSpace(creators[0].ActualId))
                    _navigation.Navigate(typeof(ArtistPage), creators[0].ActualId);
            }

            _surfaceCoordinator.Collapse();
        }
        catch
        {
        }
    }


    private async void SaveAlbumImage_Click(object sender, RoutedEventArgs e)
    {
        if (_shareSave != null)
            await _shareSave.SaveAlbumImageAsync();
    }

    private void BtnToggleWindowsMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isRealClick) return;
        _isManualChangeMode = true;
        _restoreLyricOnly = false;
        _lyricPanelVersion++;
        if (BtnToggleAlbum.IsChecked && BtnToggleLyric.IsChecked)
            _windowMode = ExpandedWindowMode.Both;
        else if (BtnToggleAlbum.IsChecked)
            _windowMode = ExpandedWindowMode.CoverOnly;
        else if (BtnToggleLyric.IsChecked) _windowMode = ExpandedWindowMode.LyricOnly;
        ChangeWindowMode();
    }

    private void BtnToggleFullScreen_Checked(object sender, RoutedEventArgs e)
    {
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
        _shareSave?.CopySongName();
    }

    private void LyricBoxContainer_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        //_lyricBox.ContextFlyout.ShowAt(_lyricBox);
        if (!_lyricBox.IsScrollPreviewing) return;
        _lyricBox.ResetScrolling();
        e.Handled = true;
    }

    private void LyricBox_ScrollPreviewChanged()
    {
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (_isCleanedUp) return;
            ShowLyricPreviewHint(_lyricBox.IsScrollPreviewing);
        });
    }

    private void ShowLyricPreviewHint(bool show)
    {
        _lyricPreviewHintStoryboard?.Pause();
        var duration = TimeSpan.FromMilliseconds(show ? 320 : 240);
        EasingFunctionBase easing = new CircleEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var opacity = new DoubleAnimation { To = show ? 1 : 0, Duration = duration, EasingFunction = easing };
        var offset = new DoubleAnimation { To = show ? 0 : -8, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(opacity, LyricPreviewHint);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTarget(offset, LyricPreviewHintOffset);
        Storyboard.SetTargetProperty(offset, "Y");
        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(offset);
        _lyricPreviewHintStoryboard = storyboard;
        storyboard.Begin();
    }

    private async void BtnLoadLocalLyric(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".qrc");
        picker.FileTypeFilter.Add(".lrc");
        picker.FileTypeFilter.Add(".yrc");
        picker.FileTypeFilter.Add(".alrc");
        picker.FileTypeFilter.Add(".ttml");
        picker.FileTypeFilter.Add(".lys");
        var sf = await picker.PickSingleFileAsync();
        if (sf != null)
        {
            var lyricInfo = await _lyricService.ImportLyricsAsync(sf, _state.NowPlayingProviderItem);
            if (lyricInfo?.PureLyricInfo is HyALRCLyricInfo alrcLyricInfo)
                _lyricBox.SetLyricLines(LrcConverter.Convert(
                    alrcLyricInfo.ALRC,
                    optimizeLyric: _lyricSettings.OptimizeLyric));
            else
                LoadLyricsBox();
        }
    }

    private async void LyricBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_windowMode == ExpandedWindowMode.LyricOnly)
        {
            UISettings uiSettings = new();
            await Task.Delay((int)uiSettings.DoubleClickTime);
            if (!_lyricBox.HasJumpedLyrics)
            {
                _windowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
            }
        }
    }

    private bool IsCurrentCover(CoverColorTicket ticket)
    {
        return _coverColorGate.IsCurrent(ticket, EffectiveCoverStream);
    }

    private async Task<CoverColors?> ExtractCoverColorsAsync(IRandomAccessStream stream)
    {
        var backgroundType = _uiSettings.ExpandedPlayerBackgroundType;
        var lyricColor = _uiSettings.LyricColor;
        var generatorType = _uiSettings.ColorGeneratorType;
        var useLab = _lyricSettings.ImpressionistLABSpace;
        var usesPalette = backgroundType is BackgroundType.Animated or BackgroundType.Isolation or BackgroundType.LikeApple;
        try
        {
            var (mainColor, palette) = await Task.Run(async () =>
            {
                var themeColor = await ColorHelper.ExtractThemeColorFromStream(stream, generatorType);
                if (!usesPalette) return (themeColor, (List<Vector3>?)null);
                stream.Seek(0);
                return (themeColor, await ColorHelper.ExtractPaletteFromStream(stream, generatorType, useLab));
            });
            var theme = mainColor;
            if (palette is { Count: > 0 })
            {
                var themeVector = Vector3.Zero;
                foreach (var item in palette) themeVector += item;
                themeVector /= palette.Count;
                theme = Color.FromArgb(255, (byte)themeVector.X, (byte)themeVector.Y, (byte)themeVector.Z);
            }

            var isBright = lyricColor is LyricColor.Auto or LyricColor.FollowCover
                ? !new Vector3(theme.R, theme.G, theme.B).RGBVectorLStarIsDark()
                : lyricColor == LyricColor.Black;
            return new CoverColors(mainColor, theme, palette, isBright,
                (0.2126 * theme.R + 0.7152 * theme.G + 0.0722 * theme.B) / 255);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyCoverColors(CoverColors colors)
    {
        var backgroundType = _uiSettings.ExpandedPlayerBackgroundType;
        _albumMainColor = colors.MainColor;
        _coverLuminance = colors.Luminance;
        if (colors.Palette is { Count: > 0 } palette)
        {
            if (backgroundType == BackgroundType.Animated)
                _albumColors =
                [
                    .. palette.Select(quantizedColor => Color.FromArgb(255, (byte)quantizedColor.X,
                        (byte)quantizedColor.Y, (byte)quantizedColor.Z))
                ];
            else if (backgroundType == BackgroundType.Isolation)
                _albumColorVectors = [.. palette.Select(t => t / 255)];
        }

        if (backgroundType is BackgroundType.CoverTheme)
            PageContainer.Background = new SolidColorBrush(colors.MainColor);
        else if (backgroundType is BackgroundType.Isolation or BackgroundType.LikeApple)
            PageContainer.Background = new SolidColorBrush(colors.Theme);
        if (backgroundType == BackgroundType.Animated)
            BlackCover.Fill = new SolidColorBrush(colors.IsBright
                ? Color.FromArgb(80, 255, 255, 255)
                : Color.FromArgb(80, 0, 0, 0));
        var dimBackground = colors.IsBright && UsesBrightDimmer();
        AnimateBrightDimmer(dimBackground);
        ApplyPlaybackTheme(ExpandedPlayerThemeFactory.Create(_uiSettings, _lyricSettings, _albumMainColor,
            colors.IsBright && !dimBackground));
        if (backgroundType == BackgroundType.LikeApple)
            _likeAppleBackgroundLayer.SetLightTheme(colors.IsBright);
        RefreshUIColor();
        if (backgroundType == BackgroundType.Animated && _albumColors.Count > 0)
        {
            Windows.UI.Xaml.Shapes.Rectangle[] cells =
                [BgRect00, BgRect01, BgRect02, BgRect10, BgRect11, BgRect12, BgRect20, BgRect21, BgRect22];
            for (var i = 0; i < cells.Length; i++)
                cells[i].Fill = new SolidColorBrush(_albumColors[i % _albumColors.Count]);
        }

        if (backgroundType == BackgroundType.Isolation)
        {
            _canvasState.AlbumColorVectors = _albumColorVectors;
            _isolationBackgroundLayer.ApplyShaderProperties();
        }
    }

    private sealed record CoverColors(
        Color MainColor,
        Color Theme,
        List<Vector3>? Palette,
        bool IsBright,
        double Luminance);

    private static string FormatPlaybackSpeed(double speed)
    {
        return "x" + speed.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ApplyPlaybackSpeed(double speed)
    {
        _control.SetPlaybackSpeed(speed);
        NowPlaybackSpeed = FormatPlaybackSpeed(_state.PlaybackSpeed);
    }

    private void BtnSpeedMinusClick(object sender, RoutedEventArgs e)
    {
        ApplyPlaybackSpeed(_state.PlaybackSpeed - 0.1);
    }

    private void BtnSpeedPlusClick(object sender, RoutedEventArgs e)
    {
        ApplyPlaybackSpeed(_state.PlaybackSpeed + 0.1);
    }

    private void TbNowSpeed_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyPlaybackSpeed(1);
    }

    private void SpeedChangesPitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
            _playbackSettings.SpeedChangesPitch = item.IsChecked;
        ApplyPlaybackSpeed(_state.PlaybackSpeed);
    }

    private void BtnCopyLyricClicked(object sender, RoutedEventArgs e)
    {
        _shareSave?.ShowLyricShareDialog();
    }

    private async void BtnToggleTinyModeClick(object sender, RoutedEventArgs e)
    {
        if (_expandedPlayerWindow is null) //判断窗口状态
        {
            _expandedPlayerWindow = await AppWindow.TryCreateAsync();
            _expandedPlayerWindow.Closed += ExpandedPlayerClosed;
        }

        if (BtnToggleTinyMode.IsChecked)
        {
            Frame expandedPlayerWindowContentFrame = new();
            expandedPlayerWindowContentFrame.Navigate(typeof(CompactPlayerPage), _expandedPlayerWindow);
            ElementCompositionPreview.SetAppWindowContent(_expandedPlayerWindow, expandedPlayerWindowContentFrame);


            _expandedPlayerWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _expandedPlayerWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            _expandedPlayerWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;

            _expandedPlayerWindow.Presenter.RequestPresentation(AppWindowPresentationKind.CompactOverlay);
            await _expandedPlayerWindow.TryShowAsync();
            _expandedPlayerWindow.Presenter.RequestPresentation(AppWindowPresentationKind.CompactOverlay); //防止进入失败
        }
        else
        {
            await _expandedPlayerWindow.CloseAsync();
        }
    }

    private void ExpandedPlayerClosed(AppWindow sender, AppWindowClosedEventArgs args)
    {
        BtnToggleTinyMode.IsChecked = false;
        _expandedPlayerWindow?.Closed -= ExpandedPlayerClosed;
    }

    internal void RestoreTitleBar() => Window.Current.SetTitleBar(AppTitleBar);

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiSettings.AlbumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(_uiSettings.AlbumBorderLength);
        _dynamicCover.UpdateLayout();
        RestoreTitleBar();
        _lifecycle.IsInBackground = false;
        if (!_isCleanedUp && !_windowSizeSubscribed)
        {
            _windowSizeSubscribed = true;
            Window.Current.SizeChanged += Current_SizeChanged;
        }

        Current_SizeChanged(null, null);
        Redesign();
        try
        {
            ViewModel.SyncFromState();
            if (_state.NowPlayingProviderItem != null)
                OnSongChange();
            _taskRunner.Forget(RefreshAlbumCoverAsync(), "refresh expanded player cover");
            ChangeWindowMode();
            _needsRedesign = true;
        }
        catch
        {
        }

        if (_uiSettings.ExpandedPlayerBackgroundType == 0 && !_uiSettings.ExpandedUseAcrylic)
            AcrylicCover.Fill = new BackdropBlurBrush { Amount = 50.0 };
        if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Animated)
        {
            AcrylicCover.Fill = new BackdropBlurBrush { Amount = 250 }; // TintAmountChange
            _luminousColorsRotateAnimation = BgRotate.CreateDoubleAnimation(
                "Angle",
                360,
                0,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(12),
                repeatBehavior: RepeatBehavior.Forever,
                autoReverse: false);
            _luminousColorsRotateStoryBoard.Children.Add(_luminousColorsRotateAnimation);
            _luminousColorsRotateStoryBoard.Begin();
        }

        NowPlaybackSpeed = FormatPlaybackSpeed(_state.PlaybackSpeed);
        switch (_uiSettings.ExpandedPlayerBackgroundType)
        {
            case BackgroundType.CoverBlur: // Default
            case BackgroundType.CoverTheme: // According to Album
                break;
            case BackgroundType.Animated:
                BlackCover.Opacity = 1;
                break;
            case BackgroundType.Isolation:
            case BackgroundType.LikeApple:
                BlackCover.Visibility = Visibility.Collapsed;
                AcrylicCover.Visibility = Visibility.Collapsed;
                break;
        }

        if (_uiSettings.AlbumRotate)
            //网易云音乐圆形唱片
            if (_state.IsPlaying)
                _ = RotateAnimationSet.StartAsync();
        if (_uiSettings.ExpandAlbumBreath) ImageAlbumAni.Begin();


        if (_bpmAniStoryboard.Children.Count > 0) _bpmAniStoryboard.Resume();

        if (_luminousColorsRotateStoryBoard.Children.Count > 0) _luminousColorsRotateStoryBoard.Resume();

        LoadLyricsBox();
    }

    private void ImageAlbum_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || !_uiSettings.EnableTouchGestureAction) return;
        double manipulationDeltaRotateValue;
        switch (_uiSettings.GestureMode)
        {
            case GestureMode.RealDJ:
                if (!_uiSettings.AlbumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                _ = _control.SeekAsync(_player.GetPosition(_player.PrimaryAudioInputNode).Add(
                    TimeSpan.FromMilliseconds((int)manipulationDeltaRotateValue) * 100));
                break;
            case GestureMode.DJ:
                if (!_uiSettings.AlbumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                return;
            case GestureMode.Shift:
                ImagePositionOffset.Y = e.Cumulative.Translation.Y / 10;
                ImagePositionOffset.X = e.Cumulative.Translation.X / 10;
                break;
            case GestureMode.Basic when Math.Abs(e.Cumulative.Translation.Y) > Math.Abs(e.Cumulative.Translation.X):
            {
                // 竖直方向滑动
                if (e.Cumulative.Translation.Y >= 0)
                    _surfaceCoordinator.UpdateExpandedFrameOffset(e.Cumulative.Translation.Y);
                else
                    ImagePositionOffset.Y = e.Cumulative.Translation.Y / 10;

                if (e.Cumulative.Translation.Y > 200)
                {
                    e.Complete();
                    _surfaceCoordinator.Collapse();
                }

                break;
            }
            case GestureMode.Basic:
            {
                ImagePositionOffset.X = e.Cumulative.Translation.X / 10;
                if (e.Cumulative.Translation.X > 400 || e.Cumulative.Translation.X < -400) e.Complete();

                break;
            }
        }
    }

    private async void ImageAlbum_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _surfaceCoordinator.ResetExpandedFrameOffset();
        if (_uiSettings.GestureMode == 0)
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
                    _ = _control.MovePreviousAndPlayAsync();
                    return;
                }

                if (e.Cumulative.Translation.X < -150)
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
                    _ = _control.MoveNextAndPlayAsync(true);
                    return;
                }
            }

        ImageResetPositionAni.Begin();
    }

    public async Task RefreshAlbumCoverAsync()
    {
        if (_isCleanedUp || _canvasResourcesReleased || _lifecycle.IsInBackground) return;
        var cover = EffectiveCoverStream;
        if (cover is null) return;
        IRandomAccessStream source;
        try
        {
            source = cover.CloneStream();
        }
        catch
        {
            return;
        }

        var ticket = _coverColorGate.Begin(cover, Stopwatch.GetTimestamp());
        var applied = false;
        try
        {
            CoverColors? colors;
            using (var sampling = source.CloneStream())
            {
                colors = await ExtractCoverColorsAsync(sampling);
            }

            if (!IsCurrentCover(ticket)) return;
            WakeLyricCanvas();
            await this.RunOnUIThreadAsync(async () => applied = await ApplyAlbumCoverAsync(ticket, source, colors));
        }
        catch
        {
        }
        finally
        {
            source.Dispose();
            _coverColorGate.Complete(ticket, applied);
            if (!ReferenceEquals(ticket.Cover, EffectiveCoverStream)) ReconcileAlbumCover();
        }
    }

    private void ReconcileAlbumCover()
    {
        if (_coverColorGate.NeedsRefresh(EffectiveCoverStream, Stopwatch.GetTimestamp())) _taskRunner.Forget(RefreshAlbumCoverAsync(), "refresh expanded player cover");
    }

    private async Task<bool> ApplyAlbumCoverAsync(CoverColorTicket ticket, IRandomAccessStream source,
        CoverColors? colors)
    {
        if (_isCleanedUp || _canvasResourcesReleased || _uiSettings.NoImage || !IsCurrentCover(ticket)) return false;
        try
        {
            using var image = source.CloneStream();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(image);
            if (_isCleanedUp || _canvasResourcesReleased || !IsCurrentCover(ticket)) return false;
            await SwapCoverAsync(bitmap, ticket);
            if (_isCleanedUp || _canvasResourcesReleased || !IsCurrentCover(ticket)) return false;
            if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.CoverBlur &&
                Background is not ImageBrush)
            {
                var brush = new ImageBrush
                    { Stretch = Stretch.UniformToFill };
                Background = brush;
            }

            if (Background is ImageBrush imageBrush) imageBrush.ImageSource = bitmap;
        }
        catch
        {
        }

        if (_isCleanedUp || _canvasResourcesReleased ||
            !_coverColorGate.TryApply(ticket, EffectiveCoverStream)) return false;
        if (colors is not null) ApplyCoverColors(colors);
        WakeLyricCanvas();
        if (_uiSettings.ExpandedPlayerBackgroundType != BackgroundType.LikeApple) return colors is not null;
        try
        {
            using var artwork = source.CloneStream();
            await _likeAppleBackgroundLayer.SetArtworkAsync(artwork);
            WakeLyricCanvas();
        }
        catch
        {
        }

        return colors is not null;
    }


    private bool UsesBrightDimmer()
    {
        return _uiSettings.LyricColor != LyricColor.Black &&
               _uiSettings.ExpandedPlayerBackgroundType != BackgroundType.LikeApple;
    }

    private void AnimateBrightDimmer(bool dim)
    {
        var amount = dim
            ? (float)Math.Clamp((_coverLuminance - 0.35) / 0.65 * 0.45, 0.08, MaxBackgroundDim)
            : 0f;
        if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Isolation)
        {
            _isolationBackgroundLayer.SetBrightDim(dim, amount);
            dim = false;
            amount = 0;
        }

        if (!dim && _dimTint is null) return;
        EnsureBackgroundDimmer();
        var compositor = _dimTint!.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
        StartDimmerFade(_dimBlur!, dim ? 1f : 0f, ease);
        StartDimmerFade(_dimTint, amount, ease);
    }

    private static void StartDimmerFade(
        Windows.UI.Composition.SpriteVisual visual,
        float target,
        Windows.UI.Composition.CompositionEasingFunction ease)
    {
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, target, ease);
        fade.Duration = TimeSpan.FromMilliseconds(900);
        visual.StartAnimation("Opacity", fade);
    }

    private void EnsureBackgroundDimmer()
    {
        if (_dimBlur is not null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(BrightDimmer).Compositor;
        var blur = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = 16,
            BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Hard,
            Source = new Windows.UI.Composition.CompositionEffectSourceParameter("backdrop")
        };
        var blurBrush = compositor.CreateEffectFactory(blur).CreateBrush();
        blurBrush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());
        _dimBlur = compositor.CreateSpriteVisual();
        _dimBlur.Brush = blurBrush;
        _dimBlur.RelativeSizeAdjustment = Vector2.One;
        _dimBlur.Opacity = 0;
        _dimTint = compositor.CreateSpriteVisual();
        _dimTint.Brush = compositor.CreateColorBrush(Colors.Black);
        _dimTint.RelativeSizeAdjustment = Vector2.One;
        _dimTint.Opacity = 0;
        var root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;
        root.Children.InsertAtTop(_dimBlur);
        root.Children.InsertAtTop(_dimTint);
        ElementCompositionPreview.SetElementChildVisual(BrightDimmer, root);
    }

    private void FadeInSongInfo()
    {
        foreach (var element in (UIElement[])[TextBlockSongTitle, SingerHyperlinkBtn, AlbumHyperlinkBtn])
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            fadeIn.InsertKeyFrame(0f, 0f);
            fadeIn.InsertKeyFrame(1f, 1f,
                compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0.2f, 1f)));
            fadeIn.Duration = TimeSpan.FromMilliseconds(480);
            visual.StartAnimation("Opacity", fadeIn);
        }
    }

    private async Task SwapCoverAsync(BitmapImage bitmap, CoverColorTicket ticket)
    {
        var swap = ++_coverSwapVersion;
        var visual = ElementCompositionPreview.GetElementVisual(ImageAlbum);
        var compositor = visual.Compositor;
        if (_hasDisplayedCover && ImageAlbum.ActualWidth > 0 && _uiSettings.ExpandAnimation)
        {
            var fadeOut = compositor.CreateScalarKeyFrameAnimation();
            fadeOut.InsertKeyFrame(1f, 0f,
                compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f)));
            fadeOut.Duration = TimeSpan.FromMilliseconds(160);
            visual.StartAnimation("Opacity", fadeOut);
            await Task.Delay(160);
            if (swap != _coverSwapVersion || !IsCurrentCover(ticket) || _isCleanedUp) return;
        }

        ViewModel.Cover = bitmap;
        _hasDisplayedCover = true;
        _dynamicCover.OnCoverDisplayed(ticket.Cover);
        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(1f, 1f,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0.2f, 1f)));
        fadeIn.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Opacity", fadeIn);
    }

    private void LuminousBackground_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_canvasResourcesReleased) return;
        _canvasResourcesReleased = true;
        _idleOverlayTimer?.Stop();
        // The control's own Unloaded follows GameLoopStopped. An ancestor's
        // Unloaded (or the page's cleanup) can precede the last Draw callback.
        var canvas = (CanvasAnimatedControl)sender;
        canvas.CreateResources -= LuminousBackground_CreateResources;
        canvas.Update -= LuminousBackground_Update;
        canvas.Draw -= LuminousBackground_Draw;
        canvas.SizeChanged -= LuminousBackground_SizeChanged;
        _canvasState.LyricBox = null;
        _lyricBox.ReleaseResources();
        _debugOverlayLayer.Dispose();
        _isolationBackgroundLayer.DisposeShader();
        _likeAppleBackgroundLayer.Dispose();
        canvas.RemoveFromVisualTree();
        LuminousBackground = null;
    }

    public Task Show()
    {
        var btnAni = new DoubleAnimation
        {
            To = 1,
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(btnAni, MoreBtn);
        Storyboard.SetTargetProperty(btnAni, "Opacity");
        storyboard.Children.Add(btnAni);
        storyboard.Begin();
        return Task.CompletedTask;
    }

    public void Collapse()
    {
        _ = this.RunOnUIThreadAsync(() =>
        {
            var btnAni = new DoubleAnimation
            {
                To = 0,
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(btnAni, MoreBtn);
            Storyboard.SetTargetProperty(btnAni, "Opacity");
            storyboard.Children.Add(btnAni);
            storyboard.Begin();
        });
    }

    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (_appleControlsActive || _isImmersive) return;
        if (!isActivated && !_playBarAutoHide.IsActive) return;
        if (isActivated)
            Show();
        else
            Collapse();
    }

    public static double Map(double value, double fromSource, double toSource, double fromTarget, double toTarget)
    {
        return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
    }

    private void LuminousBackground_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (LuminousBackground is not null) _canvasState.Overscan = (float)Math.Max(0, -LuminousBackground.Margin.Right);
        WakeLyricCanvas();
    }

    private void LuminousBackground_CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        if (_isCleanedUp || _canvasResourcesReleased) return;
        // Canvas-level configuration (not a layer concern)
        SyncCanvasState();
        SyncCanvasSettings();
        LuminousBackground.DpiScale = _lyricSettings.IsolationScale;
        RunOnUIThread(SyncCanvasRasterizationScale);
        ConfigureFramePacing();

        // Delegate to composable layers
        _expandedCanvasHost.CreateResources(sender, args);
    }

    private void LuminousBackground_Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        if (_isCleanedUp) return;
        SyncCanvasState();
        if (_canvasState.FramePacer.MarkUpdate(Stopwatch.GetTimestamp())) _expandedCanvasHost.Update(sender, args);
        TryPauseIdleLyricCanvas();
    }

    private void LuminousBackground_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (_isCleanedUp) return;
        SyncCanvasState();
        var pacer = _canvasState.FramePacer;
        var drawStartedAt = Stopwatch.GetTimestamp();
        var frame = pacer.BeginFrame(drawStartedAt);
        var session = args.DrawingSession;
        _expandedCanvasHost.Draw(sender, session, new CanvasTimingInformation
        {
            UpdateCount = frame.FrameCount,
            TotalTime = TimeSpan.FromTicks(frame.TotalTicks),
            ElapsedTime = TimeSpan.FromTicks(frame.ElapsedTicks),
            IsRunningSlowly = args.Timing.IsRunningSlowly
        });
        session.Dispose();
        var drawnAt = Stopwatch.GetTimestamp();
        _canvasState.LastFrameDrawMilliseconds = _canvasState.ShowDebugOverlay
            ? Stopwatch.GetElapsedTime(drawStartedAt, drawnAt).TotalMilliseconds
            : 0;
        ApplyFrameSchedule(sender, pacer.EndFrame(drawnAt));
    }

    private void ApplyFrameSchedule(ICanvasAnimatedControl sender, CanvasLoopSchedule schedule)
    {
        if (schedule.IsFixedTimeStep && schedule.TargetElapsedTicks != _appliedTargetElapsedTicks)
        {
            sender.TargetElapsedTime = TimeSpan.FromTicks(schedule.TargetElapsedTicks);
            _appliedTargetElapsedTicks = schedule.TargetElapsedTicks;
        }

        if (schedule.IsFixedTimeStep != _appliedFixedTimeStep)
        {
            sender.IsFixedTimeStep = schedule.IsFixedTimeStep;
            _appliedFixedTimeStep = schedule.IsFixedTimeStep;
        }

        if (schedule.ResetElapsedTime) sender.ResetElapsedTime();
    }

    private void ConfigureFramePacing()
    {
        _canvasState.FramePacer.Configure(!_lyricSettings.IsolationFullThrottle, _lyricSettings.IsolationFPS);
    }


    private void LeftPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        SingleViewModeToggle();
    }

    private void LyricView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _lyricBox.OnDoubleTapped(sender, e);
    }

    private void LyricView_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_expandedCanvasHost.TryHandlePointer(e))
            _lyricBox.LyricView_OnPointerExited(sender, e);
    }

    private void LyricView_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_expandedCanvasHost.TryHandlePointer(e))
            _lyricBox.LyricView_OnPointerMoved(sender, e);
    }

    private void LyricView_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_expandedCanvasHost.TryHandlePointer(e))
            _lyricBox.LyricView_OnPointerPressed(sender, e);
    }

    private void LyricView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_expandedCanvasHost.TryHandlePointer(e))
            _lyricBox.LyricView_PointerReleased(sender, e);
    }

    private void OnLyricEffectSettingsChanged(object? sender, LyricRenderSettingsChangedEventArgs args)
    {
        _lyricBox.SetRenderSettings(args.Settings);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay)
            _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
        if (ApplicationView.GetForCurrentView().IsFullScreenMode)
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        CleanupPageReferences();
    }

    private void CleanupPageReferences()
    {
        if (_isCleanedUp) return;
        _isCleanedUp = true;
        _idleOverlayTimer?.Stop();

        _secondTickListener.Detach();
        _enteredForegroundListener.Detach();
        _playBarVisibilityListener.Detach();
        _stateChangedListener.Detach();
        _lyricSettingsChangedListener.Detach();
        _uiSettingsChangedListener.Detach();
        _playbackSettingsChangedListener.Detach();
        _surfaceStoreChangedListener.Detach();
        _escapeRequestedListener.Detach();
        _seekRequestedListener.Detach();
        ViewModel.Dispose();
        _dynamicCover.Dispose();
        _lyricRenderSettings.SettingsChanged -= OnLyricEffectSettingsChanged;
        if (_windowSizeSubscribed)
        {
            _windowSizeSubscribed = false;
            Window.Current.SizeChanged -= Current_SizeChanged;
        }

        _lyricBox.OnBeforeRender -= LyricBox_OnBeforeRender;
        _lyricBox.ScrollPreviewChanged -= LyricBox_ScrollPreviewChanged;
        _lyricBox.OnChanged -= WakeLyricCanvas;
        _lyricBox.OnLyricLineClicked -= LyricBoxOnOnRequestSeek;
        if (_uiSettings.AlbumRotate)
            RotateAnimationSet.Stop();
        if (_uiSettings.ExpandAlbumBreath) ImageAlbumAni?.Stop();
        _expandedPlayerWindow?.Closed -= ExpandedPlayerClosed;
        _expandedPlayerWindow = null;
        _player.EnableFFTProcessing = _playbackSettings.EnableFFT || _playbackSettings.ShowSpectrum;
    }
}
