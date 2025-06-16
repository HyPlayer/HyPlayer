using CommunityToolkit.WinUI.Media;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using LyricParser.Abstraction;
using System;
using System.Collections.Generic;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class CompactPlayerPage : Page, IDisposable
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
    private List<Run> WordTextBlocks = new();
    private Dictionary<Run, Storyboard> BlockToAnimation = new();

    private bool disposedValue;


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
        //CompactPlayerAni.Begin();
    }

    private void Player_OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            PlayStateIcon.Glyph =
                HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
                    ? "\uF8AE" :
                    "\uF5B0";
        });
    }

    private async void HyPlayList_OnSongCoverChanged(HyPlayItem playItem, IBuffer coverStream)
    {
        if (HyPlayList.CoverStream.Size == 0) return;
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(coverStream);
            stream.Seek(0);
            if (!Common.Setting.noImage && stream.Size != 0)
            {
                try
                {
                    if (playItem != HyPlayList.NowPlayingItem) return;
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
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
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
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            IconLiked.Foreground = isLiked
                ? new SolidColorBrush(Colors.Red)
                : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
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
        if (HyPlayList.LyricInfo.Lyrics.Count <= HyPlayList.LyricPos) return;
        if (HyPlayList.LyricInfo.Lyrics[HyPlayList.LyricPos].LyricLine is KaraokeLyricsLine kara)
        {
            LyricControl.QuickRenderMode = false;
            if (kara.Duration.TotalSeconds > 1)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { ChangeLyric(); });
                return;
            }
        }
        else if (HyPlayList.LyricPos < HyPlayList.LyricInfo.Lyrics.Count - 1 && HyPlayList.LyricInfo.Lyrics[HyPlayList.LyricPos + 1].LyricLine is LrcLyricsLine lrcLine)
        {
            if (lrcLine.StartTime.TotalSeconds - HyPlayList.LyricInfo.Lyrics[HyPlayList.LyricPos].LyricLine.StartTime.TotalSeconds > 1)
            {
                LyricControl.QuickRenderMode = false;
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { ChangeLyric(); });
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

        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            LyricText = HyPlayList.LyricInfo.Lyrics[HyPlayList.LyricPos].LyricLine.CurrentLyric;
            LyricControl.Lyric = HyPlayList.LyricInfo.Lyrics[HyPlayList.LyricPos];
        });

    }

    private void OnChangePlayItem(HyPlayItem item)
    {
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            NowPlayingName = item?.PlayItem?.Name;
            NowPlayingArtists = item?.PlayItem?.ArtistString;
        });
        if (item.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            var isLiked = Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.Id);
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
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
        HyPlayList_OnSongCoverChanged(HyPlayList.NowPlayingItem, HyPlayList.CoverBuffer);
        PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uEDB4" : "\uEDB5";
        //Common.BarPlayBar.Visibility = Visibility.Collapsed;
        (e.Parameter as AppWindow).TitleBar.ExtendsContentIntoTitleBar = true;
        //Window.Current.SetTitleBar(MainGrid);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
        //Common.BarPlayBar.Visibility = Visibility.Visible;
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

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
            }
            HyPlayList.OnPlayPositionChange -=
            position => _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => NowProgress = position.TotalMilliseconds);
            HyPlayList.OnPlayItemChange -= OnChangePlayItem;
            HyPlayList.OnSongCoverChanged -= HyPlayList_OnSongCoverChanged;
            HyPlayList.OnLyricChange -= OnLyricChanged;
            HyPlayList.OnSongLikeStatusChange -= HyPlayList_OnSongLikeStatusChange;
            Common.OnPlaybarVisibilityChanged -= OnPlaybarVisibilityChanged;
            HyPlayList.Player.OnGlobalPlaybackStatusChanged -= Player_OnGlobalPlaybackStatusChanged;
            disposedValue = true;
        }
    }

    ~CompactPlayerPage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
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
