using CommunityToolkit.WinUI.Media;
using HyPlayer.Classes;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.HyPlayControl;
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


    private readonly SolidColorBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);
    public bool _lyricIsKaraokeLyric;
    public SongLyric Lrc;

    public CompactPlayerPage()
    {
        InitializeComponent();
        HyPlayList.OnSongCoverChanged += HyPlayList_OnSongCoverChanged;
        HyPlayList.OnPlayPositionChange += HyPlayList_OnPlayPositionChange;
        HyPlayList.OnPlayItemChange += OnChangePlayItem;
        HyPlayList.OnLyricChange += OnLyricChanged;
        HyPlayList.Player.OnGlobalPlaybackStatusChanged += Player_OnGlobalPlaybackStatusChanged;
        //LeaveAnimation.Completed += LeaveAnimation_Completed;
        HyPlayList.OnSongLikeStatusChange += HyPlayList_OnSongLikeStatusChange;
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
        Unloaded += CompactPlayerPage_Unloaded;
        //CompactPlayerAni.Begin();
    }

    private void CompactPlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        HyPlayList.OnPlayPositionChange -= HyPlayList_OnPlayPositionChange;
        HyPlayList.OnPlayItemChange -= OnChangePlayItem;
        HyPlayList.OnSongCoverChanged -= HyPlayList_OnSongCoverChanged;
        HyPlayList.OnLyricChange -= OnLyricChanged;
        HyPlayList.OnSongLikeStatusChange -= HyPlayList_OnSongLikeStatusChange;
        Common.OnPlaybarVisibilityChanged -= OnPlaybarVisibilityChanged;
        HyPlayList.Player.OnGlobalPlaybackStatusChanged -= Player_OnGlobalPlaybackStatusChanged;
    }

    private void Player_OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        _ = Common.Invoke(() =>
        {
            PlayStateIcon.Glyph =
                HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
                    ? "\uF8AE" :
                    "\uF5B0";
        });
    }

    private async void HyPlayList_OnSongCoverChanged(HyPlayItem playItem)
    {
        if (HyPlayList.CoverStream == null) return;
        _ = Common.Invoke(async ()  =>
        {
            if (!Common.Setting.noImage)
            {
                try
                {
                    if (playItem != HyPlayList.NowPlayingItem) return;
                    using var stream = HyPlayList.CoverStream.CloneStream();
                    await AlbumImageBrushSource.SetSourceAsync(HyPlayList.CoverStream);
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
        if (HyPlayList.LyricPos == -1) return;
        if (HyPlayList.HyLyricInfo.Lyrics.Count <= HyPlayList.LyricPos) return;
        if (HyPlayList.HyLyricInfo.Lyrics[HyPlayList.LyricPos].LyricLine is KaraokeLyricsLine kara)
        {
            LyricControl.QuickRenderMode = false;
            if (kara.Duration.TotalSeconds > 1)
            {
                _ = Common.Invoke(() => { ChangeLyric(); });
                return;
            }
        }
        else if (HyPlayList.LyricPos < HyPlayList.HyLyricInfo.Lyrics.Count - 1 && HyPlayList.HyLyricInfo.Lyrics[HyPlayList.LyricPos + 1].LyricLine is LrcLyricsLine lrcLine)
        {
            if (lrcLine.StartTime.TotalSeconds - HyPlayList.HyLyricInfo.Lyrics[HyPlayList.LyricPos].LyricLine.StartTime.TotalSeconds > 1)
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
            LyricText = HyPlayList.HyLyricInfo.Lyrics[HyPlayList.LyricPos].LyricLine.CurrentLyric;
            LyricControl.Lyric = HyPlayList.HyLyricInfo.Lyrics[HyPlayList.LyricPos];
        });

    }

    private void OnChangePlayItem(HyPlayItem item)
    {
        _ = Common.Invoke(() =>
        {
            NowPlayingName = item?.PlayItem?.Name;
            NowPlayingArtists = item?.PlayItem?.ArtistString;
        });
        if (item.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            var isLiked = Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.Id);
            _ = Common.Invoke(() =>
            {
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
                IconLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
                TotalProgress = item?.PlayItem?.LengthInMilliseconds ?? 0;
            });
        }
    }

    private void MovePrevious(object sender, RoutedEventArgs e)
    {
        HyPlayList.SongMovePrevious();
    }

    private void MoveNext(object sender, RoutedEventArgs e)
    {
        HyPlayList.SongMoveNext();
    }

    private void ChangePlayState(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.IsPlaying) HyPlayList.Player.PauseAll();
        else HyPlayList.Player.PlayAll();
        PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uF8AE" : "\uF5B0";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnChangePlayItem(HyPlayList.NowPlayingItem);
        HyPlayList_OnSongCoverChanged(HyPlayList.NowPlayingItem);
        PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uEDB4" : "\uEDB5";
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
