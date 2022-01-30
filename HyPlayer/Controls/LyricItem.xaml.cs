#region

using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using Kawazu;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class LyricItem : UserControl
{
    public readonly SongLyric Lrc;
    public bool hiding = false;
    public Color shadowColor = Color.FromArgb(255, 0, 0, 0);

    public bool showing = true;


    public LyricItem(SongLyric lrc)
    {
        InitializeComponent();
        TextBoxPureLyric.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        Lrc = lrc;
        TextBoxPureLyric.Text = Lrc.PureLyric;
        if (Lrc.HaveTranslation && Common.ShowLyricTrans)
            TextBoxTranslation.Text = Lrc.Translation;
        else
            TextBoxTranslation.Visibility = Visibility.Collapsed;

        if (Common.KawazuConv != null && Common.ShowLyricSound)
            LoadRomaji();
        else
            TextBoxSound.Visibility = Visibility.Collapsed;

        RefreshFontSize();
        OnHind();
    }

    private async void LoadRomaji()
    {
        if (Utilities.HasKana(Lrc.PureLyric))
            TextBoxSound.Text =
                await Common.KawazuConv?.Convert(Lrc.PureLyric, To.Romaji, Mode.Separated)! ?? string.Empty;
        // 这个地方是实在懒得改才用的 异步当同步
        else
            TextBoxSound.Visibility = Visibility.Collapsed;
        if (string.IsNullOrEmpty(TextBoxSound.Text))
            TextBoxSound.Visibility = Visibility.Collapsed;
    }


    public double actualsize => Common.PageExpandedPlayer == null
        ? Common.Setting.lyricSize <= 0 ? 23 : Common.Setting.lyricSize
        : Common.PageExpandedPlayer.showsize;

    public TextAlignment LyricAlignment =>
        Common.Setting.lyricAlignment ? TextAlignment.Left : TextAlignment.Center;

    private SolidColorBrush originBrush => Common.PageExpandedPlayer != null
        ? Common.PageExpandedPlayer.ForegroundAlbumBrush
        : Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush;

    public void RefreshFontSize()
    {
        TextBoxPureLyric.TextAlignment = LyricAlignment;
        TextBoxTranslation.TextAlignment = LyricAlignment;
        TextBoxSound.TextAlignment = LyricAlignment;
        TextBoxPureLyric.FontSize = showing ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        TextBoxTranslation.FontSize = showing ? actualsize + Common.Setting.lyricScaleSize : actualsize;
        TextBoxSound.FontSize = Common.Setting.romajiSize;
    }

    public void OnShow()
    {
        if (showing)
            //RefreshFontSize();
            return;
        showing = true;
        TextBoxPureLyric.FontSize = actualsize + Common.Setting.lyricScaleSize;
        TextBoxTranslation.FontSize = actualsize + Common.Setting.lyricScaleSize;
        TextBoxPureLyric.FontWeight = FontWeights.SemiBold;
        TextBoxTranslation.FontWeight = FontWeights.SemiBold;
        TextBoxPureLyric.Foreground = originBrush;
        TextBoxSound.Foreground = originBrush;
        TextBoxTranslation.Foreground = originBrush;
        shadowColor = originBrush.Color == Color.FromArgb(255, 0, 0, 0)
            ? Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 255, 255, 255)
            : Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 0, 0, 0);
    }

    public void OnHind()
    {
        if (!showing)
            //RefreshFontSize();
            return;
        showing = false;
        TextBoxPureLyric.FontSize = actualsize;
        TextBoxTranslation.FontSize = actualsize;
        TextBoxPureLyric.FontWeight = FontWeights.Normal;
        TextBoxTranslation.FontWeight = FontWeights.Normal;
        TextBoxPureLyric.Foreground = Application.Current.Resources["TextFillColorDisabledBrush"] as Brush;
        TextBoxTranslation.Foreground = Application.Current.Resources["TextFillColorDisabledBrush"] as Brush;
        TextBoxSound.Foreground = Application.Current.Resources["TextFillColorDisabledBrush"] as Brush;
        shadowColor = Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 0, 0, 0);
    }

    private void LyricItem_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        HyPlayList.Player.PlaybackSession.Position = Lrc.LyricTime;
        Common.PageExpandedPlayer.jumpedLyrics = true;
    }
}