using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Media;
using HyPlayer.Classes;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.HyPlayControl;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
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

namespace HyPlayer.Pages;

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


    private readonly IPlaylistService _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly ILyricService _lyricService = Ioc.Default.GetRequiredService<ILyricService>();

    private readonly SolidColorBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);
    public bool _lyricIsKaraokeLyric;
    public SongLyric Lrc;

    public CompactPlayerPage()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<PositionTickMessage>(this, (r, m) => ((CompactPlayerPage)r).HyPlayList_OnPlayPositionChange(m.Position));
        WeakReferenceMessenger.Default.Register<TrackChangedMessage>(this, (r, m) => ((CompactPlayerPage)r).OnChangePlayItem(m.Item));
        WeakReferenceMessenger.Default.Register<LyricIndexChangedMessage>(this, (r, m) => ((CompactPlayerPage)r).OnLyricChanged());
        _player.OnGlobalPlaybackStatusChanged += Player_OnGlobalPlaybackStatusChanged;
        //LeaveAnimation.Completed += LeaveAnimation_Completed;
        WeakReferenceMessenger.Default.Register<CoverChangedMessage>(this, (r, m) => HyPlayList_OnSongCoverChanged(m.Item));
        WeakReferenceMessenger.Default.Register<SongLikeStatusChangedMessage>(this, (r, m) => HyPlayList_OnSongLikeStatusChange(m.IsLiked));
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
        Unloaded += CompactPlayerPage_Unloaded;
        //CompactPlayerAni.Begin();
    }

    private void CompactPlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Common.OnPlaybarVisibilityChanged -= OnPlaybarVisibilityChanged;
        _player.OnGlobalPlaybackStatusChanged -= Player_OnGlobalPlaybackStatusChanged;
    }

    private void Player_OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        _ = Common.Invoke(() =>
        {
            PlayStateIcon.Glyph =
                _player.GlobalPlaybackStatus == PlaybackStatus.Playing
                    ? "\uF8AE" :
                    "\uF5B0";
        });
    }

    private async void HyPlayList_OnSongCoverChanged(HyPlayItem playItem)
    {
        if (_state.CoverStream == null) return;
        _ = Common.Invoke(async () =>
        {
            if (!Common.Setting.noImage)
            {
                try
                {
                    if (playItem != _state.NowPlayingItem) return;
                    using var stream = _state.CoverStream.CloneStream();
                    await AlbumImageBrushSource.SetSourceAsync(stream);
                }
                catch
                {

                }
            }
        });
    }

    private void HyPlayList_OnPlayPositionChange(TimeSpan position)
    {
        _ = Common.Invoke(() =>
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
    private void OnPlaybarVisibilityChanged(bool isActivated)
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
            if (!Common.Setting.CompactPlayerPageBlurStatus)
                ControlHover = TransparentBrush;
            PointerOutAni.Begin();
        }

    }

    private void HyPlayList_OnSongLikeStatusChange(bool isLiked)
    {
        _ = Common.Invoke(() =>
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
                _ = Common.Invoke(() => { ChangeLyric(); });
                return;
            }
        }
        else if (_lyricService.CurrentLyricIndex < _lyricService.CurrentLyricInfo.Lyrics.Count - 1 && _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex + 1].LyricLine is LrcLyricsLine lrcLine)
        {
            if (lrcLine.StartTime.TotalSeconds - _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine.StartTime.TotalSeconds > 1)
            {
                LyricControl.QuickRenderMode = false;
                _ = Common.Invoke(() => { ChangeLyric(); });
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

        _ = Common.Invoke(() =>
        {
            LyricText = _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine.CurrentLyric;
            LyricControl.Lyric = _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex];
        });

    }

    private void OnChangePlayItem(HyPlayItem item)
    {
        _ = Common.Invoke(() =>
        {
            NowPlayingName = item?.Name;
            NowPlayingArtists = item?.ArtistString;
        });
        if (item.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            var isLiked = Common.LikedSongs.Contains(_state.NowPlayingItem?.Id);
            _ = Common.Invoke(() =>
            {
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
                IconLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
                TotalProgress = item?.LengthInMilliseconds ?? 0;
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
        OnChangePlayItem(_state.NowPlayingItem);
        HyPlayList_OnSongCoverChanged(_state.NowPlayingItem);
        PlayStateIcon.Glyph = _control.IsPlaying ? "\uEDB4" : "\uEDB5";
        //Common.BarPlayBar.Visibility = Visibility.Collapsed;
        (e.Parameter?.As<AppWindow>()).TitleBar.ExtendsContentIntoTitleBar = true;
        //Window.Current.SetTitleBar(MainGrid);
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        Common.Setting.CompactPlayerPageBlurStatus = !Common.Setting.CompactPlayerPageBlurStatus;
    }

    private void LikeButton_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LikeSong();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
        //Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer), false);
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
