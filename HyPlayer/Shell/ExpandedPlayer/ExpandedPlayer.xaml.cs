#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Devices.Input;
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
using HyPlayer.Features.Playback.Services;
using HyPlayer.LyricRenderer;
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

    public static readonly DependencyProperty NowPlaybackSpeedProperty = DependencyProperty.Register(
        "NowPlaybackSpeed", typeof(string), typeof(ExpandedPlayer),
        new PropertyMetadata("x1"));

    private readonly BackgroundShaderLayer _backgroundShaderLayer;
    private readonly ExpandedCanvasState _canvasState = new();
    private readonly WeakEventListener<ExpandedPlayer, object?, EventArgs> _enteredForegroundListener;
    private readonly ExpandedCanvasHost _expandedCanvasHost = new();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly HttpClient _httpClient = Ioc.Default.GetRequiredService<HttpClient>();
    private readonly IAppLifecycleStateService _lifecycle = Ioc.Default.GetRequiredService<IAppLifecycleStateService>();
    private readonly LyricRenderView _lyricBox = new();

    private readonly ILyricEffectProfileService _lyricEffectProfiles =
        Ioc.Default.GetRequiredService<ILyricEffectProfileService>();

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
    private readonly SpectrumLayer _spectrumLayer;
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _stateChangedListener;

    private readonly IPlaybackSurfaceCoordinator _surfaceCoordinator =
        Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();

    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _surfaceStoreChangedListener;
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();


    private readonly Storyboard _bpmAniStoryboard = new();

    private readonly Storyboard _luminousColorsRotateStoryBoard = new();
    private List<Color> _albumColors = [];
    private List<Vector3> _albumColorVectors = [];
    private bool _isCleanedUp;
    private bool _isManualChangeMode;
    private bool _isRealClick;
    private SingleSongBase? _lastCoverSong;
    private int _lastHeight;
    private int _lastWidth;
    private bool _lyricHasBeenLoaded;
    private bool _lyricIsCleaning;
    private readonly List<SongLyric> _lyricList = [];
    private bool _needsRedesign = true;
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
        ViewModel = Ioc.Default.GetRequiredService<ExpandedPlayerViewModel>();
        _lyricBox.SetEffectProfile(_lyricEffectProfiles.EffectiveProfile);
        _lyricEffectProfiles.ProfileChanged += OnLyricEffectProfileChanged;
        _canvasState.LyricBox = _lyricBox;
        SyncCanvasState();
        _backgroundShaderLayer = new BackgroundShaderLayer(_canvasState, _lyricSettings);
        _spectrumLayer = new SpectrumLayer(_canvasState, _player);
        _lyricsLayer = new LyricsLayer(_canvasState);
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
        _seekRequestedListener = new WeakEventListener<ExpandedPlayer, object?, SeekRequestedEventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.HyPlayList_OnManualSeek(),
            OnDetachAction = weakEventListener => { _control.SeekRequested -= weakEventListener.OnEvent; }
        };
        _control.SeekRequested += _seekRequestedListener.OnEvent;
        Window.Current.SizeChanged += Current_SizeChanged;
        _lyricBox.Context.LineRollingEaseCalculator = new ElasticEaseRollingCalculator();
        _lyricBox.OnBeforeRender += LyricBox_OnBeforeRender;
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
            _ => new ElasticEaseRollingCalculator()
        };
        _lyricBox.Context.Effects.TransliterationScanning = _lyricSettings.LyricRenderTransliterationScanning;
        _lyricBox.Context.Effects.SimpleLineScanning = _lyricSettings.LyricRenderSimpleLineScanning;
        _lyricBox.Context.Effects.ScanStyle = _lyricSettings.LyricRenderScanStyle;
        _lyricBox.Context.PreferTypography.Font = _lyricSettings.LyricFontFamily;
        _lyricBox.Context.LineSpacing = _lyricSettings.LyricLineSpacing;

        _expandedCanvasHost.AddLayer(_backgroundShaderLayer);
        _expandedCanvasHost.AddLayer(_spectrumLayer);
        _expandedCanvasHost.AddLayer(_lyricsLayer);

        // ── Stage 8: Share/save controller ────────────────────────────
        _shareSave = new ExpandedPlayerShareSaveController(
            _state, _httpClient, _playCore, _notification,
            () => TextBlockSongTitle.Text);
    }

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

    private void SyncCanvasState()
    {
        _canvasState.BackgroundType = _uiSettings.ExpandedPlayerBackgroundType;
        _canvasState.IsPlaying = _state.IsPlaying;
        _canvasState.EnableFft = _playbackSettings.EnableFFT;
        _canvasState.WindowMode = _windowMode;
        _canvasState.AlbumColorVectors = _albumColorVectors;
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.IsPlaying):
                if (_state.IsPlaying) HyPlayList_OnPlay();
                else HyPlayList_OnPause();
                break;
            case nameof(PlaybackStateService.NowPlayingProviderItem):
            case nameof(PlaybackStateService.NowPlayingSnapshot):
                OnSongChange();
                break;
            case nameof(PlaybackStateService.CoverStream):
                RefreshAlbumCover(_state.NowPlayingProviderItem);
                break;
            case nameof(PlaybackStateService.LyricInfo):
                HyPlayList_OnLyricLoaded();
                break;
        }
    }

    private void OnSurfaceStorePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackSurfaceStore.TransitionRequestId):
                if (_surfaceStore.RequestedTransition == ExpandedPlayerTransition.Expand)
                    StartExpandAnimation();
                else
                    StartCollapseAnimation();
                break;
            case nameof(PlaybackSurfaceStore.ExpandedFrameOffsetY):
                // MainPage observes the same store value and moves the frame. ExpandedPlayer keeps the gesture local state here.
                break;
        }
    }

    private void HyPlayList_OnManualSeek()
    {
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

    private void LyricBox_OnBeforeRender(LyricRenderView view)
    {
        view.Context.IsPlaying = _state.IsPlaying;
        var primaryAudioInputNode = _player.PrimaryAudioInputNode;
        if (primaryAudioInputNode == null)
        {
            view.Context.CurrentLyricTime = 0;
            return;
        }

        var positionMilliseconds = (long)primaryAudioInputNode.Position.TotalMilliseconds;
        if (positionMilliseconds < view.Context.CurrentLyricTime)
        {
            view.Context.CurrentLyricTime = positionMilliseconds;
            _lyricBox.ReflowTime(0);
        }
        else
        {
            view.Context.CurrentLyricTime = positionMilliseconds;
        }

        view.Context.IsSeek = _positionChangedBySeeking;
        _positionChangedBySeeking = false;
    }

    public void SingleViewModeToggle()
    {
        if (_windowMode == ExpandedWindowMode.Both) return;
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
            RunOnUIThread(Redesign);
        }
    }

    private void HyPlayList_OnLyricLoaded()
    {
        LoadLyricsBox();
        _needsRedesign = true;
    }

    private void Current_SizeChanged(object? sender, WindowSizeChangedEventArgs? e)
    {
        _nowWidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
        _nowHeight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
        if (_lastWidth != _nowWidth)
        {
            LyricWidth = CalculateLyricWidth();
            LyricShowSize = _uiSettings.LyricSize <= 0
                ? Math.Max(_nowWidth / 40, 40)
                : _uiSettings.LyricSize;

            _lastWidth = _nowWidth;
            _needsRedesign = true;
        }
        else if (_lastHeight != _nowHeight)
        {
            _lastHeight = _nowHeight;
            _needsRedesign = true;
        }
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

        _needsRedesign = true;
        _isRealClick = true;
    }

    private void Redesign()
    {
        // 这个函数里面放无法用XAML实现的页面布局方式
        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;

        if (Math.Abs(_lastChangedLyricWidth - LyricWidth) > 0.001f &&
            Math.Abs(_canvasState.LyricRenderXOffset - RightPanel.ActualOffset.X) > 0.001f)
        {
            _canvasState.LyricRenderXOffset = RightPanel.ActualOffset.X;
            _canvasState.LyricRenderYOffset = RightPanel.ActualOffset.Y;
            _lyricBox.Redesign((float)LyricWidth, _nowHeight, LuminousBackground.Dpi);
            _lyricBox.ChangeRenderFontSize((float)LyricShowSize,
                _lyricSettings.TranslationSize > 0 ? _lyricSettings.TranslationSize : (float)LyricShowSize / 2,
                _lyricSettings.RomajiSize > 0 ? _lyricSettings.RomajiSize : (float)LyricShowSize / 2);
            _lastChangedLyricWidth = LyricWidth;
        }

        // 响应式布局: 窗口宽度 <= ResponsiveBreakpointWidth 时仅显示封面, > ResponsiveBreakpointWidth 时恢复双栏
        if (!_isManualChangeMode)
        {
            if (_nowWidth <= ResponsiveBreakpointWidth && _windowMode == ExpandedWindowMode.Both)
            {
                _windowMode = ExpandedWindowMode.CoverOnly;
                ChangeWindowMode();
            }
            else if (_nowWidth > ResponsiveBreakpointWidth && _windowMode != ExpandedWindowMode.Both)
            {
                _windowMode = ExpandedWindowMode.Both;
                ChangeWindowMode();
            }
        }
    }

    private void ImageAlbum_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ImageRotateTransform.CenterX = e.NewSize.Width / 2;
        ImageRotateTransform.CenterY = e.NewSize.Height / 2;
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
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (_lyricIsCleaning) return;
            if (_state.LyricInfo.PureLyricInfo is not HyALRCLyricInfo alrcLyricInfo)
                _lyricBox.SetLyricLines(LrcConverter.Convert(
                    Utils.ConvertToALRC(_state.LyricInfo.Lyrics,
                        _player.PrimaryAudioInputNode?.Duration.TotalMilliseconds ?? 0),
                    _state.LyricInfo.LyricMetadata,
                    _state.LyricInfo.SongMetadata,
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
            if (_state.NowPlayingProviderItem == null) return;
            RefreshUIColor();
            Redesign();
            _lyricHasBeenLoaded = true;
        });
    }

    internal void OnEnteringForeground()
    {
        OnSongChange();
        RefreshAlbumCover(_state.NowPlayingProviderItem);
        if (!_lyricHasBeenLoaded) HyPlayList_OnLyricLoaded();
    }

    public void OnSongChange()
    {
        _ = this.RunOnUIThreadAsync(() =>
        {
            ViewModel.SyncFromState();
            if (_player.PrimaryPlaybackSource != null)
                NowPlaybackSpeed = "x" + _player.GetPlaybackSourceSpeed(_player.PrimaryPlaybackSource);
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
        if (anim2 != null) anim3.Configuration = new DirectConnectedAnimationConfiguration();
        anim2?.Configuration = new DirectConnectedAnimationConfiguration();
        if (anim2 != null) anim1.Configuration = new DirectConnectedAnimationConfiguration();
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

    public void StartCollapseAnimation()
    {
        try
        {
            if (_uiSettings.ExpandAnimation)
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
        catch (Exception ex)
        {
            Debug.WriteLine($"Expanded player collapse animation failed: {ex}");
        }
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

    private async Task<bool> IsBrightAsync(IRandomAccessStream stream)
    {
        _lastCoverSong = _state.NowPlayingProviderItem;
        var finalResult = false; //在不手动指定背景类型为2至5时需要执行颜色采样
        var resultGenerated = false; //标志返回颜色已经生成
        if (_uiSettings.LyricColor != LyricColor.Auto && _uiSettings.LyricColor != LyricColor.FollowCover)
        {
            finalResult = _uiSettings.LyricColor == LyricColor.Black;
            resultGenerated = true;
        }

        if (_state.NowPlayingProviderItem == null) return false;
        try
        {
            var theme = await ColorHelper.ExtractThemeColorFromStream(stream);
            _albumMainColor = theme;
            if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Animated ||
                _uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Isolation)
            {
                var palette = await ColorHelper.ExtractPaletteFromStream(stream);
                if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Animated)
                {
                    _albumColors =
                    [
                        .. palette.Select(quantizedColor => Color.FromArgb(255, (byte)quantizedColor.X,
                            (byte)quantizedColor.Y, (byte)quantizedColor.Z))
                    ];
                }
                else
                {
                    _albumColorVectors = [.. palette.Select(t => t / 255)];
                    _canvasState.AlbumColorVectors = _albumColorVectors;
                }

                var themeVector = Vector3.Zero;
                foreach (var item in palette) themeVector += item;
                themeVector /= palette.Count;
                theme = Color.FromArgb(255, (byte)themeVector.X, (byte)themeVector.Y, (byte)themeVector.Z);
            }

            if (_uiSettings.ExpandedPlayerBackgroundType is BackgroundType.CoverTheme)
                PageContainer.Background =
                    new SolidColorBrush(_albumMainColor!.Value);
            if (!resultGenerated)
            {
                finalResult = !new Vector3(theme.R, theme.G, theme.B).RGBVectorLStarIsDark();
                resultGenerated = true;
            }
        }
        catch
        {
            if (!resultGenerated)
            {
                finalResult = false; //如果颜色生成失败（例如解码失败），默认使用黑色字体
                resultGenerated = true;
            }
        }

        return finalResult;
    }

    private void BtnSpeedMinusClick(object sender, RoutedEventArgs e)
    {
        var playbackSource = _player.PrimaryPlaybackSource;
        if (playbackSource == null) return;
        var currentSpeed = _player.GetPlaybackSourceSpeed(playbackSource);
        var newSpeed = Math.Max(0.5, currentSpeed - 0.1);
        _player.SetPlaybackSourceSpeed(newSpeed, playbackSource);
        NowPlaybackSpeed = "x" + newSpeed;
    }

    private void BtnSpeedPlusClick(object sender, RoutedEventArgs e)
    {
        var playbackSource = _player.PrimaryPlaybackSource;
        if (playbackSource == null) return;
        var currentSpeed = _player.GetPlaybackSourceSpeed(playbackSource);
        var newSpeed = Math.Min(2.0, currentSpeed + 0.1);
        _player.SetPlaybackSourceSpeed(newSpeed, playbackSource);
        NowPlaybackSpeed = "x" + newSpeed;
    }

    private void TbNowSpeed_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        var playbackSource = _player.PrimaryPlaybackSource;
        if (playbackSource == null) return;
        _player.SetPlaybackSourceSpeed(1, playbackSource);
        NowPlaybackSpeed = "x1";
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

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiSettings.AlbumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(_uiSettings.AlbumBorderLength);
        Window.Current.SetTitleBar(AppTitleBar);
        _lifecycle.IsInBackground = false;
        Current_SizeChanged(null, null);
        Redesign();
        try
        {
            ViewModel.SyncFromState();
            if (_state.NowPlayingProviderItem != null)
                OnSongChange();
            RefreshAlbumCover(_state.NowPlayingProviderItem);
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

        if (_player.PrimaryPlaybackSource != null)
            NowPlaybackSpeed = "x" + _player.GetPlaybackSourceSpeed(_player.PrimaryPlaybackSource);
        switch (_uiSettings.ExpandedPlayerBackgroundType)
        {
            case BackgroundType.CoverBlur: // Default
            case BackgroundType.CoverTheme: // According to Album
                break;
            case BackgroundType.Animated:
                BlackCover.Opacity = 1;
                break;
            case BackgroundType.Isolation:
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
                _ = _control.SeekAsync(_player.PrimaryAudioInputNode.Position.Add(
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

    public async void RefreshAlbumCover(SingleSongBase? playItem)
    {
        if (_state.CoverStream == null || _lifecycle.IsInBackground) return;
        using var stream = _state.CoverStream.CloneStream();
        var isBright = await IsBrightAsync(stream);
        _ = this.RunOnUIThreadAsync(async () =>
        {
            if (!_uiSettings.NoImage)
                try
                {
                    if (!ReferenceEquals(playItem, _state.NowPlayingProviderItem) ||
                        !ReferenceEquals(playItem, _lastCoverSong)) return;
                    using var cover = _state.CoverStream.CloneStream();
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(cover);
                    ViewModel.Cover = bitmap;
                    if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.CoverBlur &&
                        Background is not ImageBrush)
                    {
                        var brush = new ImageBrush
                            { Stretch = Stretch.UniformToFill };
                        Background = brush;
                    }

                    if (Background is ImageBrush imageBrush) imageBrush.ImageSource = bitmap;

                    if (!ReferenceEquals(playItem, _state.NowPlayingProviderItem) ||
                        !ReferenceEquals(playItem, _lastCoverSong)) return;
                    if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Animated && isBright)
                        BlackCover.Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                    else if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Animated && !isBright)
                        BlackCover.Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
                    ApplyPlaybackTheme(ExpandedPlayerThemeFactory.Create(_uiSettings, _lyricSettings, _albumMainColor, isBright));

                    //LoadLyricsBox();
                    RefreshUIColor();
                    if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Animated)
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

                    if (_uiSettings.ExpandedPlayerBackgroundType == BackgroundType.Isolation)
                    {
                        _canvasState.AlbumColorVectors = _albumColorVectors;
                        _backgroundShaderLayer.ApplyShaderProperties();
                    }
                }
                catch
                {
                }
        });
    }


    private void LuminousBackground_OnUnloaded(object sender, RoutedEventArgs e)
    {
        LuminousBackground.RemoveFromVisualTree();
        LuminousBackground = null;
        _backgroundShaderLayer.DisposeShader();
    }

    public Task Show()
    {
        MainGrid.Margin = new Thickness(0, 0, 0, 80);

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
            MainGrid.Margin = new Thickness(0);

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
        if (!_uiSettings.AutoHidePlaybar) return;
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
        UpdateShaderResolution();
    }

    private void UpdateShaderResolution()
    {
        _backgroundShaderLayer.UpdateResolution(
            LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualWidth, CanvasDpiRounding.Round),
            LuminousBackground.ConvertDipsToPixels((float)LuminousBackground.ActualHeight, CanvasDpiRounding.Round));
    }

    private void LuminousBackground_CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        // Canvas-level configuration (not a layer concern)
        SyncCanvasState();
        LuminousBackground.DpiScale = _lyricSettings.IsolationScale;
        if (!_lyricSettings.IsolationFullThrottle)
        {
            LuminousBackground.IsFixedTimeStep = true;
            LuminousBackground.TargetElapsedTime = TimeSpan.FromMilliseconds(16.6 * (60d / _lyricSettings.IsolationFPS));
        }

        // Delegate to composable layers
        _expandedCanvasHost.CreateResources(sender, args);
        UpdateShaderResolution();
    }

    private void LuminousBackground_Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        SyncCanvasState();
        _expandedCanvasHost.Update(sender, args);
    }

    private void LuminousBackground_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        SyncCanvasState();
        _expandedCanvasHost.Draw(sender, args);
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

    private void OnLyricEffectProfileChanged(object? sender, LyricEffectProfileChangedEventArgs args)
    {
        _lyricBox.SetEffectProfile(args.Profile);
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

        _secondTickListener.Detach();
        _enteredForegroundListener.Detach();
        _playBarVisibilityListener.Detach();
        _stateChangedListener.Detach();
        _surfaceStoreChangedListener.Detach();
        _seekRequestedListener.Detach();
        ViewModel.Dispose();
        _lyricEffectProfiles.ProfileChanged -= OnLyricEffectProfileChanged;
        Window.Current.SizeChanged -= Current_SizeChanged;
        _lyricBox.OnBeforeRender -= LyricBox_OnBeforeRender;
        _lyricBox.OnLyricLineClicked -= LyricBoxOnOnRequestSeek;
        _lyricBox.Clear();
        if (_uiSettings.AlbumRotate)
            RotateAnimationSet.Stop();
        if (_uiSettings.ExpandAlbumBreath) ImageAlbumAni?.Stop();
        _expandedPlayerWindow?.Closed -= ExpandedPlayerClosed;
        _expandedPlayerWindow = null;
    }
}
