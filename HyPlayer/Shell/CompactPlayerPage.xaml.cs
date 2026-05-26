using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Media;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.ComponentModel;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinRT;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Shell;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class CompactPlayerPage : Page
{
    public static readonly DependencyProperty NowProgressProperty = DependencyProperty.Register(
        "NowProgress", typeof(double), typeof(CompactPlayerPage), new PropertyMetadata(default(double)));

    public static readonly DependencyProperty TotalProgressProperty = DependencyProperty.Register(
        "TotalProgress", typeof(double), typeof(CompactPlayerPage), new PropertyMetadata(default(double)));

    public static readonly DependencyProperty ControlHoverProperty = DependencyProperty.Register(
        "ControlHover", typeof(Brush), typeof(CompactPlayerPage),
        new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

    public static readonly DependencyProperty LyricTextProperty =
        DependencyProperty.Register("LyricText", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata("小窗模式"));

    public static readonly DependencyProperty LyricTranslationProperty =
        DependencyProperty.Register("LyricTranslation", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata("将鼠标移到窗口以查看更多功能"));

    public static readonly DependencyProperty LyricSoundProperty =
        DependencyProperty.Register("LyricSound", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata(""));

    public static readonly DependencyProperty NowPlayingNameProperty =
        DependencyProperty.Register("NowPlayingName", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NowPlayingArtistsProperty =
        DependencyProperty.Register("NowPlayingArtists", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata(string.Empty));


    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly ILyricService _lyricService = Ioc.Default.GetRequiredService<ILyricService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly WeakEventListener<CompactPlayerPage, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly WeakEventListener<CompactPlayerPage, object?, SongLikeStatusChangedEventArgs> _songLikeStatusChangedListener;
    private readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    public bool _lyricIsKaraokeLyric;
    public SongLyric Lrc;

    public CompactPlayerPage()
    {
        InitializeComponent();
        _stateChangedListener = new WeakEventListener<CompactPlayerPage, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _songLikeStatusChangedListener = new WeakEventListener<CompactPlayerPage, object?, SongLikeStatusChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.HyPlayList_OnSongLikeStatusChange(args.IsLiked),
            OnDetachAction = weakEventListener => { _auth.SongLikeStatusChanged -= weakEventListener.OnEvent; }
        };
        _auth.SongLikeStatusChanged += _songLikeStatusChangedListener.OnEvent;
        Unloaded += CompactPlayerPage_Unloaded;
        //CompactPlayerAni.Begin();
    }

    private void CompactPlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _stateChangedListener.Detach();
        _songLikeStatusChangedListener.Detach();
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.Position):
                HyPlayList_OnPlayPositionChange(_state.Position);
                break;
            case nameof(PlaybackStateService.NowPlayingProviderItem):
                OnChangePlayItem(_state.NowPlayingProviderItem);
                HyPlayList_OnSongCoverChanged(_state.NowPlayingProviderItem, _state.NowPlayingSnapshot);
                break;
            case nameof(PlaybackStateService.NowPlayingSnapshot):
                OnChangePlayItem(_state.NowPlayingProviderItem);
                break;
            case nameof(PlaybackStateService.CoverStream):
                HyPlayList_OnSongCoverChanged(_state.NowPlayingProviderItem, _state.NowPlayingSnapshot);
                break;
            case nameof(PlaybackStateService.LyricIndex):
                OnLyricChanged();
                break;
            case nameof(PlaybackStateService.IsPlaying):
                Player_OnGlobalPlaybackStatusChanged();
                break;
        }
    }

    private void Player_OnGlobalPlaybackStatusChanged()
    {
        RunOnUIThread(() =>
        {
            PlayStateIcon.Glyph =
                _player.GlobalPlaybackStatus == PlaybackStatus.Playing
                    ? "\uF8AE" :
                    "\uF5B0";
        });
    }

    private async void HyPlayList_OnSongCoverChanged(SingleSongBase? providerItem, PlaybackCurrentItemSnapshot? snapshot)
    {
        if (_state.CoverStream == null) return;
        _taskRunner.Forget(_notification.InvokeOnUIThread(async () =>
        {
            if (!_setting.noImage)
            {
                try
                {
                    if (!ReferenceEquals(providerItem, _state.NowPlayingProviderItem) ||
                        !ReferenceEquals(snapshot, _state.NowPlayingSnapshot)) return;
                    using var stream = _state.CoverStream.CloneStream();
                    await AlbumImageBrushSource.SetSourceAsync(stream);
                }
                catch
                {

                }
            }
        }), "refresh compact player cover");
    }

    private void HyPlayList_OnPlayPositionChange(TimeSpan position)
    {
        RunOnUIThread(() =>
        {
            NowProgress = position.TotalMilliseconds;
        });
    }
    /*
    private void LeaveAnimation_Completed(object sender, object e)
    {
        EnterAnimation.Begin();
        ChangeLyric();
    }
    */
    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (isActivated)
        {
            PointerOutAni.SkipToFill();
            ControlHover = new BackdropBlurBrush { Amount = 10.0 };
            PointerInAni.Begin();
        }
        else
        {
            PointerInAni.SkipToFill();
            if (!_setting.CompactPlayerPageBlurStatus)
                ControlHover = TransparentBrush;
            PointerOutAni.Begin();
        }

    }

    private void HyPlayList_OnSongLikeStatusChange(bool isLiked)
    {
        RunOnUIThread(() =>
        {
            IconLiked.Foreground = isLiked
                ? new SolidColorBrush(Colors.Red)
                : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
            IconLiked.Glyph = isLiked
                ? "\uE00B"
                : "\uE006";
        });
    }

    public double NowProgress
    {
        get => (double)GetValue(NowProgressProperty);
        set => SetValue(NowProgressProperty, value);
    }

    public double TotalProgress
    {
        get => (double)GetValue(TotalProgressProperty);
        set => SetValue(TotalProgressProperty, value);
    }

    public Brush ControlHover
    {
        get => (Brush)GetValue(ControlHoverProperty);
        set => SetValue(ControlHoverProperty, value);
    }


    public string LyricText
    {
        get => (string)GetValue(LyricTextProperty);
        set => SetValue(LyricTextProperty, value);
    }


    public string LyricTranslation
    {
        get => (string)GetValue(LyricTranslationProperty);
        set => SetValue(LyricTranslationProperty, value);
    }
    public string LyricSound
    {
        get => (string)GetValue(LyricSoundProperty);
        set => SetValue(LyricSoundProperty, value);
    }

    public string NowPlayingName
    {
        get => (string)GetValue(NowPlayingNameProperty);
        set => SetValue(NowPlayingNameProperty, value);
    }


    public string NowPlayingArtists
    {
        get => (string)GetValue(NowPlayingArtistsProperty);
        set => SetValue(NowPlayingArtistsProperty, value);
    }

    private void OnLyricChanged()
    {
        if (_lyricService.CurrentLyricIndex == -1) return;
        if (_lyricService.CurrentLyricInfo.Lyrics.Count <= _lyricService.CurrentLyricIndex) return;
        if (_lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine is KaraokeLyricsLine kara)
        {
            LyricControl.QuickRenderMode = false;
            if (kara.Duration.TotalSeconds > 1)
            {
                RunOnUIThread(() => { ChangeLyric(); });
                return;
            }
        }
        else if (_lyricService.CurrentLyricIndex < _lyricService.CurrentLyricInfo.Lyrics.Count - 1 && _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex + 1].LyricLine is LrcLyricsLine lrcLine)
        {
            if (lrcLine.StartTime.TotalSeconds - _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine.StartTime.TotalSeconds > 1)
            {
                LyricControl.QuickRenderMode = false;
                RunOnUIThread(() => { ChangeLyric(); });
                return;
            }
            else
            {
                LyricControl.QuickRenderMode = true;
            }
        }
        ChangeLyric();
    }


    private void ChangeLyric()
    {

        RunOnUIThread(() =>
        {
            LyricText = _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine.CurrentLyric;
            LyricControl.Lyric = _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex];
        });

    }

    private void OnChangePlayItem(SingleSongBase? providerItem)
    {
        providerItem ??= _state.NowPlayingProviderItem ?? _playlist.NowPlayingProviderItem;
        var snapshot = _state.NowPlayingSnapshot ?? PlaybackCurrentItemSnapshot.FromProvider(providerItem);
        RunOnUIThread(() =>
        {
            NowPlayingName = snapshot?.Name ?? providerItem?.Name ?? string.Empty;
            NowPlayingArtists = snapshot?.ArtistText ?? (providerItem?.CreatorList is { Count: > 0 } creators
                ? string.Join("; ", creators)
                : string.Empty);
        });
        if (providerItem is null && snapshot is null)
            return;

        if (snapshot?.IsLocal != true)
        {
            var songId = providerItem?.ActualId;
            var isLiked = !string.IsNullOrEmpty(songId) && _auth.LikedSongs.Contains(songId);
            var durationMs = snapshot?.Duration ?? providerItem?.Duration ?? 0;
            RunOnUIThread(() =>
            {
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
                IconLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
                TotalProgress = durationMs;
            });
        }
    }

    private async void MovePrevious(object sender, RoutedEventArgs e)
    {
        await _playlist.MovePreviousAsync();
    }

    private async void MoveNext(object sender, RoutedEventArgs e)
    {
        await _playlist.MoveNextAsync(true);
    }

    private void ChangePlayState(object sender, RoutedEventArgs e)
    {
        _control.TogglePlayPause();
        PlayStateIcon.Glyph = _control.IsPlaying ? "\uF8AE" : "\uF5B0";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnChangePlayItem(_state.NowPlayingProviderItem);
        HyPlayList_OnSongCoverChanged(_state.NowPlayingProviderItem, _state.NowPlayingSnapshot);
        PlayStateIcon.Glyph = _control.IsPlaying ? "\uEDB4" : "\uEDB5";
        (e.Parameter?.As<AppWindow>()).TitleBar.ExtendsContentIntoTitleBar = true;
        //Window.Current.SetTitleBar(MainGrid);
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        _setting.CompactPlayerPageBlurStatus = !_setting.CompactPlayerPageBlurStatus;
    }

    private void LikeButton_Click(object sender, RoutedEventArgs e)
    {
        _auth.LikeSong();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default).AsTask(),
            "exit compact overlay mode");
    }

    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(_notification.InvokeOnUIThread(action), "CompactPlayerPage UI update");
    }

    private void Grid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        OnPlaybarVisibilityChanged(true);

    }

    private void Grid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        OnPlaybarVisibilityChanged(false);
    }
}
