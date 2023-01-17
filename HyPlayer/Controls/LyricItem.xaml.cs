#region

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml.Media.Animation;
using ColorAnimation = Windows.UI.Xaml.Media.Animation.ColorAnimation;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class LyricItem : UserControl, IDisposable
{
    public SongLyric Lrc;

    public bool showing = true;

    private bool isKaraok = false;

    private List<WordInfo> Words;

    private List<TextBlock> WordTextBlocks;

    private Dictionary<TextBlock, Storyboard> BlockToAnimation;

    class WordInfo
    {
        public int StartTime { get; set; }
        public int Duration { get; set; }
        public string Word { get; set; }
    }

    public LyricItem(SongLyric lrc)
    {
        Lrc = lrc;
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
        TextBoxTranslation.TextAlignment = LyricAlignment;
        TextBoxSound.TextAlignment = LyricAlignment;
        TextBoxPureLyric.FontSize = showing ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        TextBoxTranslation.FontSize = showing ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        TextBoxSound.FontSize = Common.Setting.romajiSize;
    }

    public void RefreshWordColor(TimeSpan position)
    {
        _ = Common.Invoke(() =>
        {
            var nowPlayingWordIndex =
                Words.FindIndex(word => word.StartTime + word.Duration > position.TotalMilliseconds);
            var playedBlocks = WordTextBlocks.GetRange(0, nowPlayingWordIndex + 1).ToList();
            if (playedBlocks.Count <= 0) return;
            foreach (var playedBlock in playedBlocks.GetRange(0, playedBlocks.Count - 1))
            {
                //playedBlock.Foreground = IdleBrush;
                playedBlock.Foreground = AccentBrush;
            }

            var playingBlock = playedBlocks.Last();
            var storyboard = BlockToAnimation[playingBlock];
            if (storyboard.GetCurrentTime().Ticks == 0)
                BlockToAnimation[playingBlock].Begin();
        });
    }

    public void OnShow()
    {
        if (showing)
            //RefreshFontSize();
            return;
        showing = true;
        if (isKaraok)
        {
            HyPlayList.OnPlayPositionChange += RefreshWordColor;
            WordTextBlocks.ForEach(w => { w.FontSize = actualsize + Common.Setting.lyricScaleSize;
                w.Foreground = new SolidColorBrush(GetKaraokIdleBrush());
            });
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
        if (!showing)
            //RefreshFontSize();
            return;
        showing = false;
        if (isKaraok)
        {
            HyPlayList.OnPlayPositionChange -= RefreshWordColor;
            WordTextBlocks.ForEach(w =>
            {
                w.FontSize = actualsize;
                w.Foreground = IdleBrush;
            });
        }

        TextBoxPureLyric.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        TextBoxPureLyric.Margin = new Thickness(0);
        TextBoxTranslation.Margin = new Thickness(0);
        TextBoxPureLyric.CharacterSpacing = 0;
        TextBoxTranslation.CharacterSpacing = 0;
        TextBoxPureLyric.FontWeight = FontWeights.SemiBold;
        TextBoxTranslation.FontWeight = FontWeights.SemiBold;
        TextBoxPureLyric.Foreground = IdleBrush;
        TextBoxTranslation.Foreground = IdleBrush;
        TextBoxSound.Foreground = IdleBrush;
        //shadowColor = Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 0, 0, 0);
    }

    private void LyricItem_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        HyPlayList.Player.PlaybackSession.Position = Lrc.LyricTime;
        Common.PageExpandedPlayer.jumpedLyrics = true;
    }

    private void LyricPanel_Loaded(object sender, RoutedEventArgs e)
    {
        TextBoxPureLyric.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        TextBoxPureLyric.Text = Lrc.PureLyric ?? string.Empty;
        if (Lrc.HaveTranslation && Common.ShowLyricTrans && !string.IsNullOrWhiteSpace(Lrc.Translation))
            TextBoxTranslation.Text = Lrc.Translation;
        else
            TextBoxTranslation.Visibility = Visibility.Collapsed;

        if (Common.ShowLyricSound)
            if (!string.IsNullOrEmpty(Lrc.Romaji)) TextBoxSound.Text = Lrc.Romaji;
            else TextBoxSound.Visibility = Visibility.Collapsed;
        else
            TextBoxSound.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(Lrc.KaraokLine))
        {
            isKaraok = true;
            Words = new List<WordInfo>();
            WordTextBlocks = new List<TextBlock>();
            BlockToAnimation = new();


            var words = Regex.Split(Lrc.KaraokLine, "\\([0-9]*,[0-9]*,0\\)").ToList().Skip(1).ToList();
            var wordInfos = Regex.Matches(Lrc.KaraokLine, "\\(([0-9]*),([0-9]*),0\\)").ToList();

            for (var index = 0; index < wordInfos.Count; index++)
            {
                var wordInfo = wordInfos[index];
                var word = words[index];
                if (wordInfo.Length <= 0) continue;
                var startTime = Convert.ToInt32(wordInfo.Groups[1].Value);
                var duration = Convert.ToInt32(wordInfo.Groups[2].Value);
                Words.Add(new WordInfo
                {
                    StartTime = startTime,
                    Duration = duration,
                    Word = word
                });
                var textBlock = new TextBlock()
                {
                    Text = word,
                    FontSize = actualsize,
                    FontWeight = FontWeights.Bold,
                    Foreground = IdleBrush
                };
                textBlock.Margin = new Thickness(word.StartsWith(' ') ? actualsize / 3 : 0, 0,
                    word.EndsWith(' ') ? actualsize / 3 : 0, 0);
                WordTextBlocks.Add(textBlock);
                WordLyricContainer.Children.Add(textBlock);
                var ani = new ColorAnimation
                {
                    From = GetKaraokIdleBrush(),
                    To = GetKaraokAccentBrush(),
                    Duration = TimeSpan.FromMilliseconds(duration)
                };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(ani, textBlock);
                Storyboard.SetTargetProperty(ani, "(TextBlock.Foreground).(SolidColorBrush.Color)");
                storyboard.Children.Add(ani);
                BlockToAnimation[textBlock] = storyboard;
            }
        }

        RefreshFontSize();
        OnHind();
    }

    public void Dispose()
    {
        Words?.Clear();
        WordTextBlocks?.Clear();
        BlockToAnimation.Clear();
        Lrc = null;
    }
}