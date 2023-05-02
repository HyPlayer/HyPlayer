using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using LyricParser.Abstraction;
using Microsoft.Toolkit.Uwp.UI.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage.FileProperties;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
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

    public static readonly DependencyProperty AlbumCoverProperty = DependencyProperty.Register(
        "AlbumCover", typeof(Brush), typeof(CompactPlayerPage), new PropertyMetadata(default(Brush)));

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

    private SolidColorBrush IdleBrush => GetIdleBrush();
    private SolidColorBrush? _pureIdleBrushCache;
    private SolidColorBrush? _pureAccentBrushCache;
    private Color? _karaokIdleColorCache;
    private Color? _karaokAccentColorCache;
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


    private SolidColorBrush GetAccentBrush()
    {
        if (Common.Setting.pureLyricFocusingColor is not null)
        {
            return _pureAccentBrushCache ??= new SolidColorBrush(Common.Setting.pureLyricFocusingColor.Value);
        }
        return (Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundAccentTextBrush
            : Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush)!;
    }

    private SolidColorBrush GetIdleBrush()
    {
        if (Common.Setting.pureLyricIdleColor is not null)
        {
            return _pureIdleBrushCache ??= new SolidColorBrush(Common.Setting.pureLyricIdleColor.Value);
        }

        return (Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundIdleTextBrush
            : Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush)!;
    }

    public CompactPlayerPage()
    {
        InitializeComponent();
        HyPlayList.OnPlayPositionChange +=
            position => _ = Common.Invoke(() => NowProgress = position.TotalMilliseconds);
        HyPlayList.OnPlayItemChange += OnChangePlayItem;
        HyPlayList.OnPlay += () => _ = Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB4");
        HyPlayList.OnPause += () => _ = Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB5");
        HyPlayList.OnLyricChange += OnLyricChanged;
        HyPlayList.OnSongLikeStatusChange += HyPlayList_OnSongLikeStatusChange;
        LeaveAnimation.Completed += LeaveAnimation_Completed;
        //CompactPlayerAni.Begin();
    }

    private void LeaveAnimation_Completed(object sender, object e)
    {
        ChangeLyric();
        EnterAnimation.Begin();
    }

    private void HyPlayList_OnSongLikeStatusChange(bool isLiked)
    {
        _ = Common.Invoke(() =>
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

    public Brush AlbumCover
    {
        get => (Brush)GetValue(AlbumCoverProperty);
        set => SetValue(AlbumCoverProperty, value);
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

        _ = Common.Invoke(() =>
        {
            LeaveAnimation.Begin();
        });

    }
    private void ChangeLyric()
    {
        _ = Common.Invoke(() =>
        {
            if (HyPlayList.Lyrics.Count <= 2)
            {
                LyricText = NowPlayingName;
                LyricTranslation = NowPlayingArtists;
            }
            WordTextBlocks.Clear();
            BlockToAnimation.Clear();
            WordLyricContainer.Text = "";
            LyricText = HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.CurrentLyric;
            LyricTranslation = HyPlayList.Lyrics[HyPlayList.LyricPos].Translation;
            LyricSound = HyPlayList.Lyrics[HyPlayList.LyricPos].Romaji;
            _lyricIsKaraokeLyric = typeof(KaraokeLyricsLine) == HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.GetType();
            Lrc = HyPlayList.Lyrics[HyPlayList.LyricPos];
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
                WordLyricContainer.Visibility = Visibility.Collapsed;
                LyricTextBlock.Visibility = Visibility.Visible;
            }
        });
    }

    public void Dispose()
    {
        HyPlayList.OnPlayPositionChange -=
            position => _ = Common.Invoke(() => NowProgress = position.TotalMilliseconds);
        HyPlayList.OnPlayItemChange -= OnChangePlayItem;
        HyPlayList.OnPlay -= () => _ = Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB4");
        HyPlayList.OnPause -= () => _ = Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB5");
        HyPlayList.OnLyricChange -= OnLyricChanged;
        HyPlayList.OnSongLikeStatusChange -= HyPlayList_OnSongLikeStatusChange;
        //CompactPlayerAni.Begin();
    }

    private void OnChangePlayItem(HyPlayItem item)
    {
        _ = Common.Invoke(async () =>
        {
            NowPlayingName = item?.PlayItem?.Name;
            NowPlayingArtists = item?.PlayItem?.ArtistString;
            BitmapImage img = null;
            if (item != null)
                if (!Common.Setting.noImage)
                    if (item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                    {
                        img = new BitmapImage();
                        if (!Common.Setting.useTaglibPicture || item.PlayItem?.LocalFileTag is null || item.PlayItem.LocalFileTag.Pictures.Length == 0)
                        {
                            await img.SetSourceAsync(
                                await HyPlayList.NowPlayingStorageFile?.GetThumbnailAsync(ThumbnailMode.MusicView, 9999));
                        }
                        else
                        {
                            await img.SetSourceAsync(new MemoryStream(item.PlayItem.LocalFileTag.Pictures[0].Data.Data).AsRandomAccessStream());
                        }
                    }
                    else
                    {
                        img = new BitmapImage(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover));
                    }
            TotalProgress = item?.PlayItem?.LengthInMilliseconds ?? 0;
            AlbumCover = new ImageBrush { ImageSource = img, Stretch = Stretch.UniformToFill };
        });
        if (item.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            var isLiked = Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.Id);
            _ = Common.Invoke(() =>
            {
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
                IconLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
            });
        }
    }
    public void RefreshWordColor(TimeSpan position)
    {
        if (!_lyricIsKaraokeLyric) return;
        _ = Common.Invoke(() =>
        {
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
        if (HyPlayList.IsPlaying) HyPlayList.Player.Pause();
        else HyPlayList.Player.Play();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnChangePlayItem(HyPlayList.NowPlayingItem);
        PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uEDB4" : "\uEDB5";
        Common.BarPlayBar.Visibility = Visibility.Collapsed;
        Window.Current.SetTitleBar(MainGrid);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
        Common.BarPlayBar.Visibility = Visibility.Visible;
    }

    private void CompactPlayerPage_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ControlHover = new BackdropBlurBrush { Amount = 10.0 };
        PlayProgress.Visibility = Visibility.Visible;

    }

    private void CompactPlayerPage_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!Common.Setting.CompactPlayerPageBlurStatus)
            ControlHover = TransparentBrush;
        PlayProgress.Visibility = Visibility.Collapsed;

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
        Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer));
    }
}