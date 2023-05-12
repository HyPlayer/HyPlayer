#region

#nullable enable
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using LyricParser.Abstraction;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using ColorAnimation = Windows.UI.Xaml.Media.Animation.ColorAnimation;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class LyricItem : UserControl, IDisposable
{
    public SongLyric Lrc;

    public bool _lyricIsOnShow = true;

    private List<Run> WordTextBlocks = new();

    private Dictionary<Run, KaraokeWordInfo> KaraokeDictionary = new();

    private Dictionary<Run, Storyboard> StoryboardDictionary = new();

    public bool _lyricIsKaraokeLyric;
    public LyricItem(SongLyric lrc)
    {
        Lrc = lrc;
        _lyricIsKaraokeLyric = typeof(KaraokeLyricsLine) == lrc.LyricLine.GetType();
        InitializeComponent();
    }


    public double actualsize => Common.PageExpandedPlayer == null
        ? Common.Setting.lyricSize <= 0 ? 23 : Common.Setting.lyricSize
        : Common.PageExpandedPlayer.showsize;

    public TextAlignment LyricAlignment =>
        Common.Setting.lyricAlignment ? TextAlignment.Left : TextAlignment.Center;
    private SolidColorBrush AccentBrush => GetAccentBrush();

    private SolidColorBrush IdleBrush => GetIdleBrush();

    private SolidColorBrush? _pureIdleBrushCache;
    private SolidColorBrush? _pureAccentBrushCache;
    private Color? _karaokIdleColorCache;
    private Color? _karaokAccentColorCache;
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

    private Color GetKaraokIdleBrush()
    {
        if (Common.Setting.karaokLyricIdleColor is not null)
        {
            return _karaokIdleColorCache ??= Common.Setting.karaokLyricIdleColor.Value;
        }

        return Common.PageExpandedPlayer != null
            ? Common.PageExpandedPlayer.ForegroundIdleTextBrush.Color
            : (Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush)!.Color;
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

    public void RefreshFontSize()
    {
        TextBoxPureLyric.TextAlignment = LyricAlignment;
        WordLyricContainer.TextAlignment = LyricAlignment;
        TextBoxTranslation.TextAlignment = LyricAlignment;
        TextBoxSound.TextAlignment = LyricAlignment;
        TextBoxPureLyric.FontSize = _lyricIsOnShow ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        WordLyricContainer.FontSize = _lyricIsOnShow ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        TextBoxTranslation.FontSize = _lyricIsOnShow ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        TextBoxSound.FontSize = Common.Setting.romajiSize;
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
            foreach (var playedBlock in playedBlocks.GetRange(0, playedBlocks.Count - 1))
            {
                //playedBlock.Foreground = IdleBrush;
                playedBlock.Foreground = new SolidColorBrush(GetKaraokAccentBrush());
<<<<<<< HEAD
                playedBlock.FontSize = actualsize + Common.Setting.lyricScaleSize;
=======
>>>>>>> parent of ae0e82b (歌词增加缩放动画)
            }

            var playingBlock = playedBlocks.Last();
            var storyboard = StoryboardDictionary[playingBlock];
            if (storyboard.GetCurrentTime().Ticks == 0)
                storyboard.Begin();
        });
<<<<<<< HEAD

=======
>>>>>>> parent of ae0e82b (歌词增加缩放动画)
    }

    public void OnShow()
    {
        if (_lyricIsOnShow)
            //RefreshFontSize();
            return;
        _lyricIsOnShow = true;
        if (_lyricIsKaraokeLyric)
        {
            WordTextBlocks.ForEach(w =>
            {
                w.Foreground = new SolidColorBrush(GetKaraokIdleBrush());
            });
            Run ani = new Run();
            foreach (var item in WordTextBlocks)
            {
                var ani = new ColorAnimation
                {
                    From = GetKaraokIdleBrush(),
                    To = GetKaraokAccentBrush(),
                    Duration = KaraokeDictionary[item].Duration,
                    EnableDependentAnimation = true
                };
<<<<<<< HEAD
                var scaleani = new DoubleAnimation
                {
                    From = actualsize,
                    To = actualsize + Common.Setting.lyricScaleSize,
                    Duration = KaraokeDictionary[item].Duration * 0.9,
                    EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
                    EnableDependentAnimation = true
                };
=======
>>>>>>> parent of ae0e82b (歌词增加缩放动画)
                var storyboard = new Storyboard();
                Storyboard.SetTarget(ani, item);
                Storyboard.SetTargetProperty(ani, "(Run.Foreground).(SolidColorBrush.Color)");
                storyboard.Children.Add(ani);
                StoryboardDictionary.Add(item, storyboard);
            }
            HyPlayList.OnPlayPositionChange += RefreshWordColor;
        }

<<<<<<< HEAD
        double durationInSeconds = 0.3;
        var transstoryboard = new Storyboard();
        var transscaleani = new DoubleAnimation
        {
            From = actualsize,
            To = actualsize + Common.Setting.lyricScaleSize - 3,
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        var transcolorani = new ColorAnimation
        {
            From = GetKaraokIdleBrush(),
            To = GetKaraokAccentBrush(),
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(transscaleani, TextBoxTranslation);
        Storyboard.SetTarget(transcolorani, TextBoxTranslation);
        Storyboard.SetTargetProperty(transscaleani, "(TextBox.FontSize)");
        Storyboard.SetTargetProperty(transcolorani, "(TextBox.Foreground).(SolidColorBrush.Color)");
        transstoryboard.Children.Add(transscaleani);
        transstoryboard.Children.Add(transcolorani);
        transstoryboard.Begin();
        var purestoryboard = new Storyboard();
        var purescaleani = new DoubleAnimation
        {
            From = actualsize,
            To = actualsize + Common.Setting.lyricScaleSize,
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        var purecolorani = new ColorAnimation
        {
            From = GetKaraokIdleBrush(),
            To = GetKaraokAccentBrush(),
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(purescaleani, TextBoxPureLyric);
        Storyboard.SetTarget(purecolorani, TextBoxPureLyric);
        Storyboard.SetTargetProperty(purescaleani, "(TextBox.FontSize)");
        Storyboard.SetTargetProperty(purecolorani, "(TextBox.Foreground).(SolidColorBrush.Color)");
        purestoryboard.Children.Add(purescaleani);
        purestoryboard.Children.Add(purecolorani);
        purestoryboard.Begin();
        TextBoxPureLyric.FontWeight = FontWeights.Bold;
        WordLyricContainer.FontWeight = FontWeights.Bold;
        TextBoxTranslation.FontWeight = FontWeights.Bold;
        TextBoxPureLyric.Margin = new Thickness(0, 20, 0, 2);
        TextBoxTranslation.Margin = new Thickness(0, 3, 0, 20);
=======
        TextBoxPureLyric.FontSize = actualsize + Common.Setting.lyricScaleSize;
        TextBoxTranslation.FontSize = actualsize + Common.Setting.lyricScaleSize;
        TextBoxPureLyric.FontWeight = FontWeights.Bold;
        WordLyricContainer.FontWeight = FontWeights.Bold;
        TextBoxTranslation.FontWeight = FontWeights.Bold;
        TextBoxPureLyric.Margin = new Thickness(0, 2, 0, 2);
        TextBoxTranslation.Margin = new Thickness(0, 2, 0, 2);
>>>>>>> parent of ae0e82b (歌词增加缩放动画)
        TextBoxPureLyric.CharacterSpacing = 30;
        TextBoxTranslation.CharacterSpacing = 30;
        TextBoxPureLyric.Foreground = AccentBrush;
        TextBoxSound.Foreground = AccentBrush;
        TextBoxTranslation.Foreground = AccentBrush;
        // shadowColor = AccentBrush.Color == Color.FromArgb(255, 0, 0, 0)
        //     ? Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 255, 255, 255)
        //     : Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 0, 0, 0);
    }

    public void OnHind()
    {
        if (!_lyricIsOnShow)
            //RefreshFontSize();
            return;
        _lyricIsOnShow = false;
        if (_lyricIsKaraokeLyric)
        {
            HyPlayList.OnPlayPositionChange -= RefreshWordColor;
            WordTextBlocks.ForEach(w =>
            {
                w.Foreground = IdleBrush;
            });
            foreach (var item in WordTextBlocks)
            {
                var wordstoryboard = new Storyboard();
                var wordscaleani = new DoubleAnimation
                {
                    To = actualsize - 5,
                    From = actualsize + Common.Setting.lyricScaleSize,
                    Duration = new Duration(TimeSpan.FromSeconds(0.8)),
                    EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
                    EnableDependentAnimation = true
                };
                var wordcolorani = new ColorAnimation
                {
                    To = GetKaraokIdleBrush(),
                    From = GetKaraokAccentBrush(),
                    Duration = new Duration(TimeSpan.FromSeconds(0.8)),
                    EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(wordscaleani, item);
                Storyboard.SetTarget(wordcolorani, item);
                Storyboard.SetTargetProperty(wordscaleani, "(item.FontSize)");
                Storyboard.SetTargetProperty(wordcolorani, "(item.Foreground).(SolidColorBrush.Color)");
                wordstoryboard.Children.Add(wordscaleani);
                wordstoryboard.Children.Add(wordcolorani);
                wordstoryboard.Begin();

            }
            StoryboardDictionary.Clear();
        }
<<<<<<< HEAD
        double durationInSeconds = 0.8;
        var purestoryboard = new Storyboard();
        var purescaleani = new DoubleAnimation
        {
            To = actualsize,
            From = actualsize + Common.Setting.lyricScaleSize,
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        var purecolorani = new ColorAnimation
        {
            To = GetKaraokIdleBrush(),
            From = GetKaraokAccentBrush(),
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(purescaleani, TextBoxPureLyric);
        Storyboard.SetTarget(purecolorani, TextBoxPureLyric);
        Storyboard.SetTargetProperty(purescaleani, "(TextBox.FontSize)");
        Storyboard.SetTargetProperty(purecolorani, "(TextBox.Foreground).(SolidColorBrush.Color)");
        purestoryboard.Children.Add(purescaleani);
        purestoryboard.Children.Add(purecolorani);
        purestoryboard.Begin();
        var transstoryboard = new Storyboard();
        var transscaleani = new DoubleAnimation
        {
            To = actualsize - 5,
            From = actualsize + Common.Setting.lyricScaleSize,
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        var colorani = new ColorAnimation
        {
            To = GetKaraokIdleBrush(),
            From = GetKaraokAccentBrush(),
            Duration = new Duration(TimeSpan.FromSeconds(durationInSeconds)),
            EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(transscaleani, TextBoxTranslation);
        Storyboard.SetTarget(colorani, TextBoxTranslation);
        Storyboard.SetTargetProperty(transscaleani, "(TextBoxtranslation.FontSize)");
        Storyboard.SetTargetProperty(colorani, "(TextBoxtranslation.Foreground).(SolidColorBrush.Color)");
        transstoryboard.Children.Add(transscaleani);
        transstoryboard.Children.Add(colorani);
        transstoryboard.Begin();
        //TextBoxPureLyric.FontSize = actualsize;
        //WordLyricContainer.FontSize = actualsize;
        TextBoxPureLyric.Margin = new Thickness(0, -5, 0, 0);
        WordLyricContainer.Margin = new Thickness(0, -5, 0, 0);
        TextBoxTranslation.Margin = new Thickness(0, 0, 0, -5);
=======

        TextBoxPureLyric.FontSize = actualsize;
        WordLyricContainer.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        TextBoxPureLyric.Margin = new Thickness(0);
        WordLyricContainer.Margin = new Thickness(0);
        TextBoxTranslation.Margin = new Thickness(0);
>>>>>>> parent of ae0e82b (歌词增加缩放动画)
        TextBoxPureLyric.CharacterSpacing = 0;
        WordLyricContainer.CharacterSpacing = 0;
        TextBoxTranslation.CharacterSpacing = 0;
        TextBoxPureLyric.FontWeight = FontWeights.SemiBold;
        WordLyricContainer.FontWeight = FontWeights.SemiBold;
        TextBoxTranslation.FontWeight = FontWeights.SemiBold;
        TextBoxPureLyric.Foreground = IdleBrush;
        WordLyricContainer.Foreground = IdleBrush;
        TextBoxTranslation.Foreground = IdleBrush;
        TextBoxSound.Foreground = IdleBrush;
        //shadowColor = Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 0, 0, 0);
    }

    private void LyricItem_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        HyPlayList.Player.PlaybackSession.Position = Lrc.LyricLine.StartTime;
        if (Common.PageExpandedPlayer != null)
        {
            Common.PageExpandedPlayer.jumpedLyrics = true;
        }
    }

    private void LyricPanel_Loaded(object sender, RoutedEventArgs e)
    {
        TextBoxPureLyric.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        WordLyricContainer.FontSize = actualsize;
        TextBoxPureLyric.Text = Lrc.LyricLine.CurrentLyric ?? string.Empty;
        if (Lrc.HaveTranslation && Common.ShowLyricTrans && !string.IsNullOrWhiteSpace(Lrc.Translation))
            TextBoxTranslation.Text = Lrc.Translation;
        else
            TextBoxTranslation.Visibility = Visibility.Collapsed;

        if (Common.ShowLyricSound)
            if (!string.IsNullOrEmpty(Lrc.Romaji)) TextBoxSound.Text = Lrc.Romaji;
            else TextBoxSound.Visibility = Visibility.Collapsed;
        else
            TextBoxSound.Visibility = Visibility.Collapsed;

        if (_lyricIsKaraokeLyric)
        {

            foreach (var item in ((KaraokeLyricsLine)Lrc.LyricLine).WordInfos)
            {
                var textBlock = new Run()
                {
                    Text = item.CurrentWords,
                    Foreground = IdleBrush
                };
                WordTextBlocks.Add(textBlock);
                WordLyricContainer.Inlines.Add(textBlock);
                KaraokeDictionary[textBlock] = item;
            }
        }
        RefreshFontSize();
        OnHind();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                WordTextBlocks.Clear();
                KaraokeDictionary.Clear();
                StoryboardDictionary.Clear();
            }
            if (_lyricIsKaraokeLyric) HyPlayList.OnPlayPositionChange -= RefreshWordColor;
            disposedValue = true;
        }
    }

    ~LyricItem()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}