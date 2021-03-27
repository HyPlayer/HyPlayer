using HyPlayer.Classes;
using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using HyPlayer.HyPlayControl;
using Kawazu;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{

    public sealed partial class LyricItem : UserControl
    {
        public readonly SongLyric Lrc;
        public double actualsize => Common.PageExpandedPlayer == null ? 18 : Common.PageExpandedPlayer.showsize;
        private readonly Brush originBrush;

        public bool showing = false;
        public bool hiding = false;
        public LyricItem(SongLyric lrc)
        {
            InitializeComponent();
            originBrush = TextBoxPureLyric.Foreground;
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
            Lrc = lrc;
            TextBoxPureLyric.Text = Lrc.PureLyric;
            if (Lrc.HaveTranslation && Common.ShowLyricTrans)
            {
                TextBoxTranslation.Text = Lrc.Translation;
            }
            else
            {
                TextBoxTranslation.Visibility = Visibility.Collapsed;
            }

            if (Common.KawazuConv != null && Common.ShowLyricSound)
            {
                Task.Run(() =>
                {
                    Invoke((async () =>
                    {
                        if (Kawazu.Utilities.HasKana(Lrc.PureLyric))
                        {
                            TextBoxSound.Text = await Common.KawazuConv.Convert(Lrc.PureLyric, To.Romaji, Mode.Separated);
                        }

                    }));
                });
            }
            else
            {
                TextBoxSound.Visibility = Visibility.Collapsed;
            }

            OnHind();
        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        public void OnShow()
        {
            showing = true;
            TextBoxPureLyric.FontWeight = FontWeights.SemiBold;
            TextBoxTranslation.FontWeight = FontWeights.SemiBold;
            TextBoxPureLyric.Foreground = originBrush;
            TextBoxSound.Foreground = originBrush;
            TextBoxTranslation.Foreground = originBrush;
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
        }

        public void OnHind()
        {
            showing = false;
            TextBoxPureLyric.FontWeight = FontWeights.Normal;
            TextBoxTranslation.FontWeight = FontWeights.Normal;
            TextBoxPureLyric.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
            TextBoxTranslation.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
            TextBoxSound.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
        }

        private void LyricItem_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            HyPlayList.Player.PlaybackSession.Position = Lrc.LyricTime;
        }
    }
}
