#region

#nullable enable annotations
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Animations;
using CommunityToolkit.WinUI.Media;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Album;
using HyPlayer.Features.Artist;
using HyPlayer.Features.Radio;
using HyPlayer.Features.User;
using HyPlayer.Infrastructure.Imaging;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;
using HyPlayer.Shell.Playback;
using HyPlayer.UI.Dialogs;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Impressionist.Abstractions;
using Impressionist.Implementations;
using Microsoft.Graphics.Canvas;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Graphics.Imaging;
using Windows.Storage;
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
using LrcConverter = HyPlayer.Domain.Lyrics.LrcConverter;
using UISettings = Windows.UI.ViewManagement.UISettings;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Shell.ExpandedPlayer;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ExpandedPlayer : Page
{
    public static readonly DependencyProperty NowPlaybackSpeedProperty = DependencyProperty.Register(
        "NowPlaybackSpeed", typeof(string), typeof(ExpandedPlayer),
        new PropertyMetadata("x1"));

    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly IAppLifecycleStateService _lifecycle = Ioc.Default.GetRequiredService<IAppLifecycleStateService>();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly IPlayBarAutoHideService _playBarAutoHide = Ioc.Default.GetRequiredService<IPlayBarAutoHideService>();
    private readonly HttpClient _httpClient = Ioc.Default.GetRequiredService<HttpClient>();
    private readonly IPlaybackSurfaceCoordinator _surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly WeakEventListener<ExpandedPlayer, object?, EventArgs> _secondTickListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, EventArgs> _enteredForegroundListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, PlayBarVisibilityChangedEventArgs> _playBarVisibilityListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, PropertyChangedEventArgs> _surfaceStoreChangedListener;
    private readonly WeakEventListener<ExpandedPlayer, object?, SeekRequestedEventArgs> _seekRequestedListener;

    // Services accessed via ViewModel; shortcuts for code-behind convenience
    private PlayCoreBase _playCore => ViewModel.PlayCore;
    private IPlaybackControlService _control => ViewModel.Control;
    private PlaybackStateService _state => ViewModel.State;
    private ILyricService _lyricService => ViewModel.LyricService;

    public bool jumpedLyrics;
    public double lastChangedLyricWidth;
    private bool _lyricHasBeenLoaded;
    private bool _lyricIsCleaning;
    private bool _positionChangedBySeeking;
    private int _lastHeight;
    private int _lastWidth;
    public SingleSongBase? _lastSong;
    private SingleSongBase? _lastCoverSong;
    private bool _isManualChangeMode;
    private bool _needsRedesign = true;
    private int _nowHeight;
    private int _nowWidth;
    private bool _isRealClick;
    private ExpandedWindowMode _windowMode;
    private AppWindow? expandedPlayerWindow;
    public Color? albumMainColor;
    public int _stopwatch = 3;
    private ExpandedPlayerShareSaveController? _shareSave;
    private readonly ExpandedCanvasState _canvasState = new();
    private readonly ExpandedCanvasHost _expandedCanvasHost = new();
    private readonly BackgroundShaderLayer _backgroundShaderLayer;
    private readonly SpectrumLayer _spectrumLayer;
    private readonly LyricsLayer _lyricsLayer;
    public List<SongLyric> _lyricList = [];
    private readonly LyricRenderView _lyricBox = new();
    private Setting _settings => ViewModel.Settings;
    private List<Vector3> _albumColorVectors = [];
    private List<Color> _albumColors = [];
    private PlaybackThemeSnapshot _playbackTheme = PlaybackThemeSnapshot.Default;
    private const float LyricBoxRightPadding = 32;
    private const double ResponsiveBreakpointWidth = 800;
    private bool _isCleanedUp;

    public double LyricShowSize { get; set; }
    public double LyricWidth { get; set; }
    public string NowPlaybackSpeed
    {
        get => (string)GetValue(NowPlaybackSpeedProperty);
        set => SetValue(NowPlaybackSpeedProperty, value);
    }

    public ExpandedPlayerViewModel ViewModel { get; }

    public SolidColorBrush PlaybackAccentBrush => _playbackTheme.AccentBrush;

    private void SyncCanvasState()
    {
        _canvasState.BackgroundType = _settings.expandedPlayerBackgroundType;
        _canvasState.IsPlaying = _state.IsPlaying;
        _canvasState.EnableFft = _settings.EnableFFT;
        _canvasState.IsolationLightWave = _settings.IsolationLightWave;
        _canvasState.WindowMode = _windowMode;
        _canvasState.AlbumColorVectors = _albumColorVectors;
    }

    public ExpandedPlayer()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<ExpandedPlayerViewModel>();
        _canvasState.LyricBox = _lyricBox;
        SyncCanvasState();
        _backgroundShaderLayer = new BackgroundShaderLayer(_canvasState);
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
        _playBarVisibilityListener = new WeakEventListener<ExpandedPlayer, object?, PlayBarVisibilityChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybarVisibilityChanged(args.IsActivated),
            OnDetachAction = weakEventListener => { _playBarAutoHide.VisibilityChanged -= weakEventListener.OnEvent; }
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
        _lyricBox.OnBeforeRender += _lyricBox_OnBeforeRender;
        _lyricBox.OnLyricLineClicked += _lyricBoxOnOnRequestSeek;
        _lyricBox.Context.LyricWidthRatio = _settings.lyricRenderWidthRatio / 100f;
        _lyricBox.Context.LyricPaddingTopRatio = _settings.lyricPaddingTopRatio / 100f;
        _lyricBox.Context.CurrentLyricTime = 0;
        _lyricBox.Context.Debug = _settings.LyricRendererDebugMode;
        _lyricBox.Context.Effects.Blur = _settings.lyricRenderBlur;
        _lyricBox.Context.Effects.CacheRenderTarget = _settings.lyricCacheRenderTarget;
        _lyricBox.Context.LineRollingEaseCalculator = _settings.LineRollingCalculator switch
        {
            RollingCalculator.SinRollingCalculator => new SinRollingCalculator(),
            RollingCalculator.LyricifyRollingCalculator => new LyricifyRollingCalculator(),
            RollingCalculator.SyncRollingCalculator => new SyncRollingCalculator(),
            RollingCalculator.CircleEaseRollingCalculator => new CircleEaseRollingCalculator(),
            _ => new ElasticEaseRollingCalculator()
        };
        _lyricBox.Context.Effects.ScaleWhenFocusing = _settings.lyricRenderScaleWhenFocusing;
        _lyricBox.Context.Effects.FocusHighlighting = _settings.lyricRenderFocusHighlighting;
        _lyricBox.Context.Effects.TransliterationScanning = _settings.lyricRenderTransliterationScanning;
        _lyricBox.Context.Effects.SimpleLineScanning = _settings.lyricRenderSimpleLineScanning;
        _lyricBox.Context.PreferTypography.Font = _settings.lyricFontFamily;
        _lyricBox.Context.LineSpacing = _settings.lyricLineSpacing;

        _expandedCanvasHost.AddLayer(_backgroundShaderLayer);
        _expandedCanvasHost.AddLayer(_spectrumLayer);
        _expandedCanvasHost.AddLayer(_lyricsLayer);

        // ── Stage 8: Share/save controller ────────────────────────────
        _shareSave = new ExpandedPlayerShareSaveController(
            _state, _httpClient, _playCore, _notification,
            () => TextBlockSongTitle.Text);
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.IsPlaying):
                if (_state.IsPlaying) HyPlayList_OnPlay(); else HyPlayList_OnPause();
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

    private void _lyricBoxOnOnRequestSeek(RenderingLyricLine line)
    {
        jumpedLyrics = true;
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
                _taskRunner.Forget(Windows.System.Launcher.LaunchUriAsync(new Uri(action)).AsTask(), "launch lyric action uri");
            }
        }
        else
        {
            _taskRunner.Forget(_control.SeekAsync(TimeSpan.FromMilliseconds(line.StartTime)), "seek from lyric click");
        }
    }

    private void _lyricBox_OnBeforeRender(LyricRenderer.LyricRenderView view)
    {
        view.Context.IsPlaying = _player.GlobalPlaybackStatus == PlaybackStatus.Playing;
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
        _windowMode = _windowMode == ExpandedWindowMode.LyricOnly ? ExpandedWindowMode.CoverOnly : ExpandedWindowMode.LyricOnly;
        ChangeWindowMode();
    }
    private void HyPlayList_OnPlay()
    {
        _ = _notification.InvokeOnUIThread(() =>
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
        _ = _notification.InvokeOnUIThread(() =>
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
            LyricShowSize = _settings.lyricSize <= 0
                ? Math.Max(_nowWidth / 40, 40)
                : _settings.lyricSize;

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

        if (Math.Abs(lastChangedLyricWidth - LyricWidth) > 0.001f && Math.Abs(_canvasState.LyricRenderXOffset - RightPanel.ActualOffset.X) > 0.001f)
        {
            _canvasState.LyricRenderXOffset = RightPanel.ActualOffset.X;
            _canvasState.LyricRenderYOffset = RightPanel.ActualOffset.Y;
            _lyricBox.Redesign((float)LyricWidth, _nowHeight, LuminousBackground.Dpi);
            _lyricBox.ChangeRenderFontSize((float)LyricShowSize,
                (_settings.translationSize > 0) ? _settings.translationSize : (float)LyricShowSize / 2,
                (_settings.romajiSize > 0) ? _settings.romajiSize : (float)LyricShowSize / 2);
            lastChangedLyricWidth = LyricWidth;
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
        _taskRunner.Forget(_notification.InvokeOnUIThread(action), "ExpandedPlayer UI update");
    }

    private readonly Storyboard luminousColorsRotateStoryBoard = new();
    private DoubleAnimation luminousColorsRotateAnimation = new();


    private readonly Storyboard bpmAniStoryboard = new();

    public void LoadLyricsBox()
    {
        _ = _notification.InvokeOnUIThread(() =>
        {
            if (_lyricIsCleaning) return;
            if (_state.LyricInfo.PureLyricInfo is not HyALRCLyricInfo alrcLyricInfo)
            {
                _lyricBox.SetLyricLines(LrcConverter.Convert(Utils.ConvertToALRC(_state.LyricInfo.Lyrics, _player.PrimaryAudioInputNode?.Duration.TotalMilliseconds ?? 0), _state.LyricInfo.LyricMetadata, _state.LyricInfo.SongMetadata));
            }
            else
            {
                _lyricBox.SetLyricLines(LrcConverter.Convert(alrcLyricInfo.ALRC, alrcLyricInfo.LyricMetadata, alrcLyricInfo.SongMetadata));
            }
            _lyricBox.ChangeAlignment(_settings.lyricAlignment switch
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
        var providerItem = _state.NowPlayingProviderItem;
        var lyricIsReady = ReferenceEquals(_lastSong, providerItem);
        _lyricHasBeenLoaded = lyricIsReady;
        _ = _notification.InvokeOnUIThread(() =>
        {
            ViewModel.SyncFromState();
            if (providerItem == null)
            {
                _lyricList.Clear();
            }

            if (providerItem == null) return;

            if (!lyricIsReady)
            {
                if (!_lyricHasBeenLoaded)
                {
                    //歌词加载中提示
                    _lyricIsCleaning = true;
                    lock (_lyricList)
                    {
                        _lyricList.Clear();
                        _lyricList.Add(SongLyric.LoadingLyric);
                    }
                    _lyricIsCleaning = false;
                    if (_lyricHasBeenLoaded)
                    {
                        LoadLyricsBox();
                    }
                }
            }

            _needsRedesign = true;
            if (_player.PrimaryPlaybackSource != null)
                NowPlaybackSpeed = "x" + _player.GetPlaybackSourceSpeed(_player.PrimaryPlaybackSource);
        });
    }

    public void RefreshUIColor()
    {
        _lyricBox.ChangeRenderColor(_playbackTheme.IdleBrush.Color, _playbackTheme.AccentBrush.Color);
    }

    private void ApplyPlaybackTheme(PlaybackThemeSnapshot theme)
    {
        _playbackTheme = theme;
        _canvasState.IsBrightTheme = theme.IsBright;
        Bindings.Update();
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
            if (_settings.expandAnimation)
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
                _lyricBox.SetLyricLines(LrcConverter.Convert(alrcLyricInfo.ALRC));
            else
                LoadLyricsBox();
        }
    }

    private async void LyricBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_windowMode == ExpandedWindowMode.LyricOnly)
        {
            UISettings _uiSettings = new();
            await Task.Delay((int)(_uiSettings.DoubleClickTime));
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
        if (_settings.lyricColor != LyricColor.Auto && _settings.lyricColor != LyricColor.FollowCover)
        {
            finalResult = _settings.lyricColor == LyricColor.Black;
            resultGenerated = true;
        }
        if (_state.NowPlayingProviderItem == null) return false;
        try
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            var colors = await ImageDecoder.GetPixelColor(decoder);
            ThemeColorResult themeColor;
            PaletteResult palette;
            if (_settings.expandedPlayerBackgroundType != BackgroundType.Animated && _settings.expandedPlayerBackgroundType != BackgroundType.Isolation)
            {
                themeColor = KMeansPaletteGenerator.CreateThemeColor(colors, true, true);
                albumMainColor = Color.FromArgb(255, (byte)themeColor.Color.X, (byte)themeColor.Color.Y, (byte)themeColor.Color.Z);
            }
            else
            {
                palette = _settings.ColorGeneratorType switch
                {
                    ColorGeneratorType.KMeans => await KMeansPaletteGenerator.CreatePalette(
                                                colors,
                                                _settings.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                                                true,
                                                true,
                                                true),
                    ColorGeneratorType.OctTree => palette = await OctTreePaletteGenerator.CreatePalette(
                                                colors,
                                                _settings.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                                                true),
                    _ => await AutoPaletteGenerator.CreatePalette(
                                                colors,
                                                _settings.expandedPlayerBackgroundType is BackgroundType.Animated ? 9 : 4,
                                                true,
                                                true,
                                                true),
                };
                themeColor = palette.ThemeColor;
                _albumColors = [.. palette.Palette.Select(quantizedColor => Color.FromArgb(255, (byte)quantizedColor.X, (byte)quantizedColor.Y, (byte)quantizedColor.Z))];
                albumMainColor = Color.FromArgb(255, (byte)themeColor.Color.X, (byte)themeColor.Color.Y, (byte)themeColor.Color.Z);
                _albumColorVectors = [.. palette.Palette.Select(t => t / 255)];
                _canvasState.AlbumColorVectors = _albumColorVectors;
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
        if (expandedPlayerWindow is null) //判断窗口状态
        {
            expandedPlayerWindow = await AppWindow.TryCreateAsync();
            expandedPlayerWindow.Closed += ExpandedPlayerClosed;
        }

        if (BtnToggleTinyMode.IsChecked)
        {
            Frame expandedPlayerWindowContentFrame = new();
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

    }

    private void ExpandedPlayerClosed(AppWindow sender, AppWindowClosedEventArgs args)
    {
        BtnToggleTinyMode.IsChecked = false;
        expandedPlayerWindow?.Closed -= ExpandedPlayerClosed;
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ABStartPoint = _player.PrimaryAudioInputNode.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ABEndPoint = _player.PrimaryAudioInputNode.Position;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settings.albumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(_settings.albumBorderLength);
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
        if (_player.PrimaryPlaybackSource != null)
            NowPlaybackSpeed = "x" + _player.GetPlaybackSourceSpeed(_player.PrimaryPlaybackSource);
        switch (_settings.expandedPlayerBackgroundType)
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

        if (_settings.albumRotate)
            //网易云音乐圆形唱片
            if (_state.IsPlaying)
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
            case GestureMode.RealDJ:
                if (!_settings.albumRound) return;
                manipulationDeltaRotateValue = e.Delta.Rotation;
                if (manipulationDeltaRotateValue == 0) manipulationDeltaRotateValue = e.Delta.Translation.Y;
                ImageRotateTransform.Angle += manipulationDeltaRotateValue;
                _ = _control.SeekAsync(_player.PrimaryAudioInputNode.Position.Add(
                    TimeSpan.FromMilliseconds((int)manipulationDeltaRotateValue) * 100));
                break;
            case GestureMode.DJ:
                if (!_settings.albumRound) return;
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
                    {
                        _surfaceCoordinator.UpdateExpandedFrameOffset(e.Cumulative.Translation.Y);
                    }
                    else
                    {
                        ImagePositionOffset.Y = e.Cumulative.Translation.Y / 10;
                    }

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
        _surfaceCoordinator.ResetExpandedFrameOffset();
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
                    _ = _control.MovePreviousAndPlayAsync();
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
                    _ = _control.MoveNextAndPlayAsync(true);
                    return;
                }
            }
        }

        ImageResetPositionAni.Begin();
    }

    public async void RefreshAlbumCover(SingleSongBase? playItem)
    {
        if (_state.CoverStream == null || _lifecycle.IsInBackground) return;
        using var stream = _state.CoverStream.CloneStream();
        var isBright = await IsBrightAsync(stream);
        _ = _notification.InvokeOnUIThread(async () =>
        {
            if (!_settings.noImage)
            {
                try
                {
                    if (!ReferenceEquals(playItem, _state.NowPlayingProviderItem) || !ReferenceEquals(playItem, _lastCoverSong)) return;
                    using var cover = _state.CoverStream.CloneStream();
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(cover);
                    ViewModel.Cover = bitmap;
                    if (_settings.expandedPlayerBackgroundType == BackgroundType.CoverBlur && Background is not ImageBrush)
                    {
                        var brush = new ImageBrush()
                        { Stretch = Stretch.UniformToFill };
                        Background = brush;
                    }
                    if (Background is ImageBrush imageBrush)
                    {
                        imageBrush.ImageSource = bitmap;
                    }

                    if (!ReferenceEquals(playItem, _state.NowPlayingProviderItem) || !ReferenceEquals(playItem, _lastCoverSong)) return;
                    if (_settings.expandedPlayerBackgroundType == BackgroundType.Animated && isBright)
                        BlackCover.Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                    else if (_settings.expandedPlayerBackgroundType == BackgroundType.Animated && !isBright)
                        BlackCover.Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
                    ApplyPlaybackTheme(ExpandedPlayerThemeFactory.Create(_settings, albumMainColor, isBright));

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
                        _canvasState.AlbumColorVectors = _albumColorVectors;
                        _backgroundShaderLayer.ApplyShaderProperties();
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
        _backgroundShaderLayer.DisposeShader();
    }
    public Task Show()
    {
        MainGrid.Margin = new Thickness(0, 0, 0, 80);

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

    public void Collapse()
    {
        _ = _notification.InvokeOnUIThread(() =>
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
    }

    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (!_settings.AutoHidePlaybar) return;
        if (isActivated)
        {
            Show();
        }
        else
        {
            Collapse();
        }
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

    private void LuminousBackground_CreateResources(Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        // Canvas-level configuration (not a layer concern)
        SyncCanvasState();
        LuminousBackground.DpiScale = _settings.IsolationScale;
        if (!_settings.IsolationFullThrottle)
        {
            LuminousBackground.IsFixedTimeStep = true;
            LuminousBackground.TargetElapsedTime = TimeSpan.FromMilliseconds(16.6 * (60d / _settings.IsolationFPS));
        }

        // Delegate to composable layers
        _expandedCanvasHost.CreateResources(sender, args);
        UpdateShaderResolution();
    }

    private void LuminousBackground_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
    {
        SyncCanvasState();
        _expandedCanvasHost.Update(sender, args);
    }

    private void LuminousBackground_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
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
        Window.Current.SizeChanged -= Current_SizeChanged;
        _lyricBox.OnBeforeRender -= _lyricBox_OnBeforeRender;
        _lyricBox.OnLyricLineClicked -= _lyricBoxOnOnRequestSeek;
        if (_settings.albumRotate)
            RotateAnimationSet.Stop();
        if (_settings.expandAlbumBreath)
        {
            ImageAlbumAni?.Stop();
        }
        if (expandedPlayerWindow is not null)
        {
            expandedPlayerWindow.Closed -= ExpandedPlayerClosed;
            expandedPlayerWindow = null;
        }
    }
}
