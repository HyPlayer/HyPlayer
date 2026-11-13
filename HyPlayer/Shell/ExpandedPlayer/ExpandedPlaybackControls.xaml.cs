using System;
using System.ComponentModel;
using System.Numerics;
using Windows.System.Profile;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Xaml;
using HyPlayer.UI.Controls.Primitives;
using HyPlayer.UI.Playback.PlayBar;

namespace HyPlayer.Shell.ExpandedPlayer;

public sealed partial class ExpandedPlaybackControls : UserControl
{
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly PlaybackStateService _playbackState = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private bool _isAttached;
    private const float IconSwapScale = 0.6f;

    private long _lastElapsedSecond = -1;
    private long _lastRemainingSecond = -1;
    private bool? _qualityHostBright;
    private int _qualityVersion;
    private bool _showingTransition;
    private TextGlowEffect? _textGlow;
    private readonly Windows.UI.ViewManagement.UISettings _systemSettings = new();
    private readonly PressSpring _playModeSpring;
    private readonly PressSpring _previousSpring;
    private readonly PressSpring _playPauseSpring;
    private readonly PressSpring _nextSpring;

    public ExpandedPlaybackControls()
    {
        InitializeComponent();
        ProgressBar.ValueDragging += ProgressBar_ValueDragging;
        ProgressBar.ValueCommitted += ProgressBar_ValueCommitted;
        VolumeBar.ValueDragging += VolumeBar_ValueChanged;
        VolumeBar.ValueCommitted += VolumeBar_ValueChanged;
        _playModeSpring = new PressSpring(BtnPlayMode, AnimationsEnabled);
        _previousSpring = new PressSpring(BtnPrevious, AnimationsEnabled);
        _playPauseSpring = new PressSpring(BtnPlayPause, AnimationsEnabled);
        _nextSpring = new PressSpring(BtnNext, AnimationsEnabled);
        _ = new PressSpring(BtnMore, AnimationsEnabled);
        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            ItemDesktopLyrics.Visibility = Visibility.Collapsed;
    }

    public ExpandedPlayerViewModel? ViewModel
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            Detach();
            field = value;
            Bindings.Update();
            if (IsLoaded) Attach();
        }
    }

    private double DurationMilliseconds => ViewModel is not { } viewModel
        ? 0
        : viewModel.Duration != TimeSpan.Zero
            ? viewModel.Duration.TotalMilliseconds
            : viewModel.NowPlayingSnapshot?.Duration ?? 0;

    private bool AnimationsEnabled() =>
        ViewModel?.UISettings.ExpandAnimation != false && _systemSettings.AnimationsEnabled;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    private void Attach()
    {
        if (_isAttached || ViewModel is not { } viewModel) return;
        _isAttached = true;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _playbackState.PropertyChanged += PlaybackState_PropertyChanged;
        RefreshAll();
    }

    private void Detach()
    {
        if (!_isAttached || ViewModel is not { } viewModel) return;
        _isAttached = false;
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _playbackState.PropertyChanged -= PlaybackState_PropertyChanged;
    }

    private void PlaybackState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlaybackStateService.IsTransitioning)) return;
        _ = this.RunOnUIThreadAsync(() =>
        {
            if (_isAttached) RefreshQuality(true);
        });
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExpandedPlayerViewModel.Position):
                RefreshProgress();
                break;
            case nameof(ExpandedPlayerViewModel.Duration):
                _lastRemainingSecond = -1;
                RefreshProgress();
                break;
            case nameof(ExpandedPlayerViewModel.IsPlaying):
                RefreshPlayState();
                break;
            case nameof(ExpandedPlayerViewModel.IsLoadingTrack):
                RefreshPlayState();
                RefreshQuality(true);
                break;
            case nameof(ExpandedPlayerViewModel.IsInFm):
                RefreshFmState();
                RefreshPlayMode();
                break;
            case nameof(ExpandedPlayerViewModel.ActiveStrategyId):
                RefreshPlayMode();
                break;
            case nameof(ExpandedPlayerViewModel.NowPlayingSnapshot):
                _lastElapsedSecond = -1;
                _lastRemainingSecond = -1;
                RefreshProgress();
                RefreshQuality(true);
                break;
            case nameof(ExpandedPlayerViewModel.QualityTag):
                RefreshQuality(true);
                break;
            case nameof(ExpandedPlayerViewModel.Volume):
                RefreshVolume();
                break;
        }
    }

    private void RefreshAll()
    {
        _lastElapsedSecond = -1;
        _lastRemainingSecond = -1;
        RefreshProgress();
        RefreshPlayState(false);
        RefreshFmState();
        RefreshPlayMode();
        RefreshVolume();
        RefreshQuality(false);
    }

    private void RefreshPlayMode()
    {
        if (ViewModel is not { } viewModel) return;
        var (glyph, text) = viewModel.IsInFm
            ? ("\uE107", "我不喜欢")
            : viewModel.ActiveStrategyId switch
            {
                "shn" => ("\uE14B", "随机播放"),
                "sgl" => ("\uE1CC", "单曲循环"),
                _ => ("\uE169", "顺序播放")
            };
        IconPlayMode.Glyph = glyph;
        ToolTipService.SetToolTip(BtnPlayMode, text);
        AutomationProperties.SetName(BtnPlayMode, viewModel.IsInFm ? text : "播放顺序");
    }

    private void RefreshQuality(bool animate)
    {
        var transitioning = _playbackState.IsTransitioning;
        var text = transitioning
            ? "过渡"
            : ViewModel is not { NowPlayingSnapshot: { } snapshot } viewModel
                ? string.Empty
                : PlayBarViewModel.GetQualityTagText(snapshot, viewModel.QualityTag,
                    viewModel.PlaybackSettings.AudioRate, viewModel.IsLoadingTrack);
        if (QualityText.Text == text && _showingTransition == transitioning && _qualityHostBright is not null)
            return;
        _showingTransition = transitioning;
        _textGlow ??= new TextGlowEffect(QualityRevealHost, QualityMaskSource, QualityRevealText, QualityBloomHost,
            QualityFillHost);
        ProgressBar.IsGlowing = transitioning;
        AnimateQualityHost(transitioning);
        if (!transitioning) _textGlow.SetActive(false);
        var version = ++_qualityVersion;
        if (!animate || string.IsNullOrEmpty(QualityText.Text))
        {
            QualityText.Text = text;
            QualityText.Opacity = transitioning ? 0 : 1;
            if (transitioning) _textGlow.SetActive(true);
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(QualityText);
        var compositor = visual.Compositor;
        var fadeOut = compositor.CreateScalarKeyFrameAnimation();
        fadeOut.InsertKeyFrame(1f, 0f);
        fadeOut.Duration = TimeSpan.FromMilliseconds(150);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", fadeOut);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (version != _qualityVersion) return;
            QualityText.Text = text;
            QualityText.Opacity = transitioning ? 0 : 1;
            if (transitioning) _textGlow.SetActive(true);
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            fadeIn.InsertKeyFrame(1f, 1f);
            fadeIn.Duration = TimeSpan.FromMilliseconds(240);
            visual.StartAnimation("Opacity", fadeIn);
        };
    }

    private void AnimateQualityHost(bool bright)
    {
        if (_qualityHostBright == bright) return;
        var visual = ElementCompositionPreview.GetElementVisual(QualityHost);
        if (_qualityHostBright is null)
        {
            visual.Opacity = bright ? 1f : 0.6f;
            _qualityHostBright = bright;
            return;
        }

        _qualityHostBright = bright;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, bright ? 1f : 0.6f);
        fade.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation("Opacity", fade);
    }

    private void RefreshPlayState(bool animate = true)
    {
        if (ViewModel is not { } viewModel) return;
        var playing = PlayBarViewModel.ShowsPauseAction(viewModel.IsPlaying, viewModel.IsLoadingTrack);
        var glyph = playing ? "\uF8AE" : "\uF5B0";
        var changed = IconPlayPause.Glyph != glyph;
        IconPlayPause.Glyph = glyph;
        var text = playing ? "暂停" : "播放";
        AutomationProperties.SetName(BtnPlayPause, text);
        ToolTipService.SetToolTip(BtnPlayPause, text);
        if (!changed || !animate || !IsLoaded || !AnimationsEnabled()) return;
        PressSpring.Start(IconPlayPause, Vector3.One, PressSpring.ReleaseDamping, PressSpring.ReleasePeriod,
            new Vector3(IconSwapScale, IconSwapScale, 1));
    }

    private void RefreshFmState()
    {
        if (ViewModel is not { } viewModel) return;
        IconPrevious.Glyph = viewModel.IsInFm ? "\uE7E8" : "\uF8AC";
        var text = viewModel.IsInFm ? "退出私人 FM" : "上一首";
        AutomationProperties.SetName(BtnPrevious, text);
        ToolTipService.SetToolTip(BtnPrevious, text);
    }

    private void RefreshVolume()
    {
        if (ViewModel is not { } viewModel || VolumeBar.IsDragging) return;
        VolumeBar.Value = viewModel.Volume;
    }

    private void RefreshProgress()
    {
        if (ViewModel is not { } viewModel || ProgressBar.IsDragging) return;
        var duration = DurationMilliseconds;
        var position = viewModel.Position.TotalMilliseconds;
        ProgressBar.Value = duration > 0 ? position / duration : 0;
        UpdateTimeTexts(position, duration);
    }

    private void UpdateTimeTexts(double position, double duration)
    {
        position = Math.Max(0, position);
        var remaining = Math.Max(0, duration - position);
        var elapsedSecond = (long)(position / 1000);
        var remainingSecond = (long)Math.Ceiling(remaining / 1000);
        if (elapsedSecond != _lastElapsedSecond)
        {
            _lastElapsedSecond = elapsedSecond;
            ElapsedText.Text = FormatTime(elapsedSecond);
        }

        if (remainingSecond != _lastRemainingSecond)
        {
            _lastRemainingSecond = remainingSecond;
            RemainingText.Text = "-" + FormatTime(remainingSecond);
        }
    }

    private static string FormatTime(long totalSeconds)
    {
        var time = TimeSpan.FromSeconds(totalSeconds);
        return time.Hours == 0
            ? $"{time.Minutes}:{time.Seconds:00}"
            : $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
    }

    private void ProgressBar_ValueDragging(double value)
    {
        var duration = DurationMilliseconds;
        UpdateTimeTexts(value * duration, duration);
    }

    private void ProgressBar_ValueCommitted(double value)
    {
        var duration = DurationMilliseconds;
        if (duration <= 0) return;
        ViewModel?.SeekCommand.Execute(TimeSpan.FromMilliseconds(value * duration));
    }

    private void VolumeBar_ValueChanged(double value)
    {
        if (ViewModel is not { } viewModel) return;
        viewModel.Control.Volume = Math.Round(value * 100) / 100;
    }

    private void BtnPlayMode_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.ChangePlayMode);
        _playModeSpring.Pulse();
    }

    private void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.MovePrevious);
        _previousSpring.Pulse();
        if (AnimationsEnabled()) PressSpring.Nudge(IconPrevious, -1);
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.TogglePlayback);
        _playPauseSpring.Pulse();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.MoveNext);
        _nextSpring.Pulse();
        if (AnimationsEnabled()) PressSpring.Nudge(IconNext, 1);
    }

    private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void RootGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void MoreFlyout_Opening(object sender, object e)
    {
        var songId = ViewModel?.NowPlayingProviderItem?.ActualId;
        var isNetease = ViewModel?.NowPlayingSnapshot is { IsLocal: false };
        var isLiked = !string.IsNullOrEmpty(songId) && _auth.LikedSongs.Contains(songId);
        ItemLike.Text = isLiked ? "取消喜欢" : "喜欢";
        ItemLikeIcon.Glyph = isLiked ? "\uE00B" : "\uE006";
        ItemLike.Visibility = isNetease ? Visibility.Visible : Visibility.Collapsed;
        ItemSubscribe.Visibility = isNetease ? Visibility.Visible : Visibility.Collapsed;
        ItemDownload.Visibility = isNetease ? Visibility.Visible : Visibility.Collapsed;
        ItemShare.Visibility = isNetease ? Visibility.Visible : Visibility.Collapsed;
        ItemComment.Visibility = isNetease && ViewModel?.UISettings.NotClearMode == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ItemLike_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.ToggleLike);
    }

    private void ItemPlaylist_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.ShowQueue, BtnMore);
    }

    private void ItemDesktopLyrics_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.OpenDesktopLyrics);
    }

    private void ItemListenTogether_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.OpenListenTogether);
    }

    private void ItemSubscribe_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.AddToSongList);
    }

    private void ItemDownload_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.Download);
    }

    private void ItemComment_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.OpenComments);
    }

    private void ItemShare_Click(object sender, RoutedEventArgs e)
    {
        _surfaceStore.RequestCommand(PlaybackSurfaceCommand.Share);
    }
}
