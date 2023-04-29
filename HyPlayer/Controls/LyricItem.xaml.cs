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

    private Dictionary<Run, Storyboard> BlockToAnimation = new();

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
                ((KaraokeLyricsLine)Lrc.LyricLine).WordInfos.Where(word => word.StartTime <= position.TotalMilliseconds).ToList();
            var playedBlocks = WordTextBlocks.GetRange(0, playedWords.Count).ToList();
            if (playedBlocks.Count <= 0) return;
            foreach (var playedBlock in playedBlocks.GetRange(0, playedBlocks.Count - 1))
            {
                //playedBlock.Foreground = IdleBrush;
                playedBlock.Foreground = new SolidColorBrush(GetKaraokAccentBrush());
            }

            var playingBlock = playedBlocks.Last();
            var storyboard = BlockToAnimation[playingBlock];
            if (storyboard.GetCurrentTime().Ticks == 0)
                BlockToAnimation[playingBlock].Begin();
        });
    }

    public void OnShow()
    {
        if (_lyricIsOnShow)
            //RefreshFontSize();
            return;
        _lyricIsOnShow = true;
        if (_lyricIsKaraokeLyric)
        {
            WordTextBlocks?.ForEach(w =>
            {
                w.Foreground = new SolidColorBrush(GetKaraokIdleBrush());
            });
            HyPlayList.OnPlayPositionChange += RefreshWordColor;
        }

        TextBoxPureLyric.FontSize = actualsize + Common.Setting.lyricScaleSize;
        TextBoxTranslation.FontSize = actualsize + Common.Setting.lyricScaleSize;
        TextBoxPureLyric.FontWeight = FontWeights.Bold;
        TextBoxTranslation.FontWeight = FontWeights.Bold;
        TextBoxPureLyric.Margin = new Thickness(0, 2, 0, 2);
        TextBoxTranslation.Margin = new Thickness(0, 2, 0, 2);
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
            WordTextBlocks?.ForEach(w =>
            {
                w.Foreground = IdleBrush;
            });
        }

        TextBoxPureLyric.FontSize = actualsize;
        WordLyricContainer.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        TextBoxPureLyric.Margin = new Thickness(0);
        WordLyricContainer.Margin = new Thickness(0);
        TextBoxTranslation.Margin = new Thickness(0);
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
        HyPlayList.Player.PlaybackSession.Position = TimeSpan.FromMilliseconds(Lrc.LyricLine.StartTime);
        Common.PageExpandedPlayer.jumpedLyrics = true;
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
                    FontWeight = FontWeights.Bold,
                    Foreground = IdleBrush
                };
                WordTextBlocks?.Add(textBlock);
                WordLyricContainer.Inlines.Add(textBlock);
                var ani = new ColorAnimation
                {
                    From = GetKaraokIdleBrush(),
                    To = GetKaraokAccentBrush(),
                    Duration = TimeSpan.FromMilliseconds(item.Duration),
                    EnableDependentAnimation = true
                };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(ani, textBlock);
                Storyboard.SetTargetProperty(ani, "(Run.Foreground).(SolidColorBrush.Color)");
                storyboard.Children.Add(ani);
                BlockToAnimation[textBlock] = storyboard;
            }
        }
        RefreshFontSize();
        OnHind();
    }

    public void Dispose()
    {
        if (_lyricIsKaraokeLyric) HyPlayList.OnPlayPositionChange -= RefreshWordColor;
        WordTextBlocks.Clear();
        BlockToAnimation.Clear();
    }
}