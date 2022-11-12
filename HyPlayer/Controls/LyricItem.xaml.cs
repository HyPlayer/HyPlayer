#region

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
        Lrc = lrc;
        InitializeComponent();
    }


    public double actualsize => Common.PageExpandedPlayer == null
        ? Common.Setting.lyricSize <= 0 ? 23 : Common.Setting.lyricSize
        : Common.PageExpandedPlayer.showsize;

    public TextAlignment LyricAlignment =>
        Common.Setting.lyricAlignment ? TextAlignment.Left : TextAlignment.Center;

    private SolidColorBrush AccentBrush => Common.PageExpandedPlayer != null
        ? Common.PageExpandedPlayer.ForegroundAccentTextBrush
        : Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush;

    private SolidColorBrush IdleBrush => Common.PageExpandedPlayer != null
        ? Common.PageExpandedPlayer.ForegroundIdleTextBrush
        : Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush;


    private async Task LoadRomaji()
    {
        var resultSound = string.Empty;

        switch(Common.Setting.LyricRomajiSource)
        {
            case 0:
                {
                    if (!string.IsNullOrEmpty(Lrc.NeteaseRomaji)) resultSound = Lrc.NeteaseRomaji;
                    else
                    {
                        if (Utilities.HasKana(Lrc.PureLyric))
                        {
                            resultSound = await Common.KawazuConv?.Convert(Lrc.PureLyric, To.Romaji, Mode.Separated);
                        }
                    }
                    break;
                }
            case 1:
                {
                    resultSound = Lrc.NeteaseRomaji;
                    break;
                }
            case 2:
                {
                    if (Utilities.HasKana(Lrc.PureLyric))
                    {
                        resultSound = await Common.KawazuConv?.Convert(Lrc.PureLyric, To.Romaji, Mode.Separated);
                    }
                    break;
                }
        }
            
        if (!string.IsNullOrEmpty(resultSound)) TextBoxSound.Text = resultSound;
        else TextBoxSound.Visibility=Visibility.Collapsed;

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

    public void OnShow()
    {
        if (showing)
            //RefreshFontSize();
            return;
        showing = true;
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
        shadowColor = AccentBrush.Color == Color.FromArgb(255, 0, 0, 0)
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
        TextBoxPureLyric.Margin = new Thickness(0);
        TextBoxTranslation.Margin = new Thickness(0);
        TextBoxPureLyric.CharacterSpacing = 0;
        TextBoxTranslation.CharacterSpacing = 0;
        TextBoxPureLyric.FontWeight = FontWeights.SemiBold;
        TextBoxTranslation.FontWeight = FontWeights.SemiBold;
        TextBoxPureLyric.Foreground = IdleBrush;
        TextBoxTranslation.Foreground = IdleBrush;
        TextBoxSound.Foreground = IdleBrush;
        shadowColor = Color.FromArgb((byte)(Common.Setting.lyricDropshadow ? 255 : 0), 0, 0, 0);
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
            _ = LoadRomaji();
        else
            TextBoxSound.Visibility = Visibility.Collapsed;

        RefreshFontSize();
        OnHind();
    }
}