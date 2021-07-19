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

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class LyricItem : UserControl
    {
        public readonly SongLyric Lrc;
        public bool hiding = false;

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
                Task.Run(() =>
                {
                    Common.Invoke(async () =>
                    {
                        if (Utilities.HasKana(Lrc.PureLyric))
                            TextBoxSound.Text =
                                await Common.KawazuConv.Convert(Lrc.PureLyric, To.Romaji, Mode.Separated);
                        else
                            TextBoxSound.Visibility = Visibility.Collapsed;
                    });
                });
            else
                TextBoxSound.Visibility = Visibility.Collapsed;

            OnHind();
        }


        public double actualsize => Common.PageExpandedPlayer == null
            ? Common.Setting.lyricSize == 0 ? 18 : Common.Setting.lyricSize
            : Common.PageExpandedPlayer.showsize;

        public TextAlignment LyricAlignment => Common.Setting.lyricAlignment ? TextAlignment.Left : TextAlignment.Center;

        private Brush originBrush => Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as Brush;

        public void RefreshFontSize()
        {
            TextBoxPureLyric.TextAlignment = LyricAlignment;
            TextBoxTranslation.TextAlignment = LyricAlignment;
            TextBoxSound.TextAlignment = LyricAlignment;
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
        }

        public void OnShow()
        {
            if (showing)
                //RefreshFontSize();
                return;
            showing = true;
            TextBoxPureLyric.FontWeight = FontWeights.SemiBold;
            TextBoxTranslation.FontWeight = FontWeights.SemiBold;
            TextBoxPureLyric.Foreground = originBrush;
            TextBoxSound.Foreground = originBrush;
            TextBoxTranslation.Foreground = originBrush;
        }

        public void OnHind()
        {
            if (!showing)
                //RefreshFontSize();
                return;
            showing = false;
            TextBoxPureLyric.FontWeight = FontWeights.Normal;
            TextBoxTranslation.FontWeight = FontWeights.Normal;
            TextBoxPureLyric.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
            TextBoxTranslation.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
            TextBoxSound.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
        }

        private void LyricItem_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            HyPlayList.Player.PlaybackSession.Position = Lrc.LyricTime;
        }
    }
}