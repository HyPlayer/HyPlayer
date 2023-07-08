using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using LyricParser.Abstraction;
using Microsoft.Toolkit.Uwp.UI.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Text;
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

#nullable enable
    private Color? _karaokAccentColorCache;
#nullable restore
    private bool disposedValue;

    private Color GetKaraokAccentBrush()
    {
        if (Common.Setting.karaokLyricFocusingColor is not null)
        {
            return _karaokAccentColorCache ??= Common.Setting.karaokLyricFocusingColor.Value;
        }
        return Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundAccentTextBrush.Color
            : (Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush)!.Color;
    }

    public CompactPlayerPage()
    {
        InitializeComponent();
        HyPlayList.OnSongCoverChanged += HyPlayList_OnSongCoverChanged;
        HyPlayList.OnPlayPositionChange += HyPlayList_OnPlayPositionChange;
        HyPlayList.OnPlayItemChange += OnChangePlayItem;
        HyPlayList.OnLyricChange += OnLyricChanged;
        HyPlayList.OnSongLikeStatusChange += HyPlayList_OnSongLikeStatusChange;
        LeaveAnimation.Completed += LeaveAnimation_Completed;
        Common.OnPlaybarVisibilityChanged += OnPlaybarVisibilityChanged;
        //CompactPlayerAni.Begin();
    }



    private async Task HyPlayList_OnSongCoverChanged(int hashCode, IRandomAccessStream coverStream)
    {
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
        {
            using var stream = coverStream.CloneStream();
            if (!Common.Setting.noImage && stream.Size != 0)
            {
                try
                {
                    if (hashCode != HyPlayList.NowPlayingHashCode) return;
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
            if (HyPlayList.FadeProcessStatus && !HyPlayList.AutoFadeProcessing)
            {
                PlayStateIcon.Glyph =
                HyPlayList.CurrentFadeInOutState == HyPlayList.FadeInOutState.FadeIn
                    ? "\uF8AE"
                    : "\uF5B0";
            }
            else
            {
                PlayStateIcon.Glyph =
                HyPlayList.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                    ? "\uF8AE"
                    : "\uF5B0";
            }
        });
    }

    private void LeaveAnimation_Completed(object sender, object e)
    {
        ChangeLyric();
        EnterAnimation.Begin();        
    }
    private Task OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (isActivated)
        {
            PointerOutAni.SkipToFill();
            ControlHover = new BackdropBlurBrush { Amount = 10.0 };
            PointerInAni.Begin();
            return Task.CompletedTask;
        }
        else
        {
            PointerInAni.SkipToFill();
            if (!Common.Setting.CompactPlayerPageBlurStatus)
                ControlHover = TransparentBrush;
            PointerOutAni.Begin();
            return Task.CompletedTask;
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
        if (HyPlayList.Lyrics.Count <= HyPlayList.LyricPos) return;
        if (HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine is KaraokeLyricsLine kara)
        {
            if (kara.Duration.TotalSeconds > 1)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { LeaveAnimation.Begin(); });
                return;
            }
        }else if (HyPlayList.LyricPos < HyPlayList.Lyrics.Count - 1 && HyPlayList.Lyrics[HyPlayList.LyricPos+1].LyricLine is LrcLyricsLine lrcLine)
        {
            if (lrcLine.StartTime.TotalSeconds - HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.StartTime.TotalSeconds > 1)
            {
                LyricControl.QuickRenderMode = false;
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { LeaveAnimation.Begin(); });   
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
            LyricText = HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.CurrentLyric;
            LyricControl.Lyric = HyPlayList.Lyrics[HyPlayList.LyricPos];
            return;
            WordTextBlocks.Clear();
            BlockToAnimation.Clear();
            WordLyricContainer.Text = "";
            LyricTranslation = HyPlayList.Lyrics[HyPlayList.LyricPos].Translation;
            LyricSound = HyPlayList.Lyrics[HyPlayList.LyricPos].Romaji;
            
            Debug.WriteLine($"LyricChanged:{HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.CurrentLyric}");
            _lyricIsKaraokeLyric = typeof(KaraokeLyricsLine) == HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.GetType();
            Lrc = HyPlayList.Lyrics[HyPlayList.LyricPos];
            if (HyPlayList.Lyrics.Count <= 2)
            {
                LyricText = NowPlayingName;
                LyricTranslation = NowPlayingArtists;
            }
            LyricTranslationBlock.Visibility = (LyricTranslation != string.Empty && LyricTranslation != null && Common.ShowLyricTrans) ? Visibility.Visible : Visibility.Collapsed;
            LyricSoundBlock.Visibility = (LyricSound != string.Empty && LyricSound != null && Common.ShowLyricSound) ? Visibility.Visible : Visibility.Collapsed;
            if (_lyricIsKaraokeLyric)
            {
                WordLyricContainer.Visibility = Visibility.Visible;
                LyricTextBlock.Visibility = Visibility.Collapsed;

                foreach (var item in ((KaraokeLyricsLine)Lrc.LyricLine).WordInfos)
                {
                    var textBlock = new Run()
                    {
                        Text = item.CurrentWords,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(GetKaraokAccentBrush()),
                    };
                    textBlock.Foreground.Opacity = 0.4;
                    WordTextBlocks?.Add(textBlock);
                    WordLyricContainer.Inlines.Add(textBlock);
                    var ani = new DoubleAnimation
                    {
                        From = 0.4,
                        To = 1,
                        Duration = item.Duration,
                        EnableDependentAnimation = true,
                    };
                    item.Duration = (item.Duration < TimeSpan.FromMilliseconds(200)) ? TimeSpan.FromMilliseconds(200) : item.Duration;
                    ani.EasingFunction = (item.Duration >= TimeSpan.FromMilliseconds(300)) ? new SineEase { EasingMode = EasingMode.EaseOut } : new CircleEase { EasingMode = EasingMode.EaseInOut };
                    var storyboard = new Storyboard();
                    Storyboard.SetTarget(ani, textBlock);
                    Storyboard.SetTargetProperty(ani, "(Run.Foreground).(SolidColorBrush.Opacity)");
                    storyboard.Children.Add(ani);
                    BlockToAnimation[textBlock] = storyboard;
                }
                WordTextBlocks?.ForEach(w =>
                {
                    w.Foreground.Opacity = 0.4;
                });
                HyPlayList.OnPlayPositionChange += RefreshWordColor;

            }
            else
            {
                HyPlayList.OnPlayPositionChange -= RefreshWordColor;
            }
        });
        
    }

    private void OnChangePlayItem(HyPlayItem item)
    {
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            NowPlayingName = item?.PlayItem?.Name;
            NowPlayingArtists = item?.PlayItem?.ArtistString;
            PlayStateIcon.Glyph =
                HyPlayList.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                    ? "\uF8AE" :
                    "\uF5B0";
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
    public void RefreshWordColor(TimeSpan position)
    {
        if (!_lyricIsKaraokeLyric) return;

        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            LyricControl.CurrentTime = HyPlayList.Player.PlaybackSession.Position - HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.StartTime;
            var playedWords =
                ((KaraokeLyricsLine)Lrc.LyricLine).WordInfos.Where(word => word.StartTime <= position).ToList();
            var playedBlocks = WordTextBlocks.GetRange(0, playedWords.Count).ToList();
            if (playedBlocks.Count <= 0) return;
            var playingBlock = playedBlocks.Last();
            var storyboard = BlockToAnimation[playingBlock];
            if (storyboard.GetCurrentTime().Ticks == 0)
                BlockToAnimation[playingBlock].Begin();
            foreach (var playedBlock in playedBlocks.GetRange(0, playedBlocks.Count - 1))
            {
                if (((SolidColorBrush)playedBlock.Foreground).Opacity <= 0.41)
                    BlockToAnimation[playedBlock].Begin();
                //((SolidColorBrush)playedBlock.Foreground).Opacity = 1;
            }


        });
    }

    private async void MovePrevious(object sender, RoutedEventArgs e)
    {
        await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.UserNextFadeOut, HyPlayList.SongChangeType.Previous);
    }

    private async void MoveNext(object sender, RoutedEventArgs e)
    {
        await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.UserNextFadeOut, HyPlayList.SongChangeType.Next);
    }

    private async void ChangePlayState(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.IsPlaying) await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.PauseFadeOut);
        else await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.PlayFadeIn);
        PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uF8AE" : "\uF5B0";
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnChangePlayItem(HyPlayList.NowPlayingItem);
        using var coverStream = HyPlayList.CoverStream.CloneStream();
        await HyPlayList_OnSongCoverChanged(HyPlayList.NowPlayingHashCode, coverStream);
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
