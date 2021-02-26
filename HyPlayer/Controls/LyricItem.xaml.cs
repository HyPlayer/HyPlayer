using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using Microsoft.UI.Xaml.Media;
using AcrylicBackgroundSource = Windows.UI.Xaml.Media.AcrylicBackgroundSource;
using AcrylicBrush = Microsoft.UI.Xaml.Media.AcrylicBrush;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    
    public sealed partial class LyricItem : UserControl
    {
        public readonly SongLyric Lrc;
        public double actualsize => Common.PageExpandedPlayer.showsize;
        private Brush originBrush;

        public bool showing = false;
        public bool hiding = false;
        public LyricItem(SongLyric lrc)
        {

            this.InitializeComponent();
            originBrush = TextBoxPureLyric.Foreground;
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
            Lrc = lrc;
            TextBoxPureLyric.Text = Lrc.PureLyric;
            if (Lrc.HaveTranslation)
            {
                TextBoxTranslation.Text = Lrc.Translation;
            }
            else
            {
                TextBoxTranslation.Visibility = Visibility.Collapsed;
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
            TextBoxTranslation.Foreground = originBrush;
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
        }

        public void OnHind()
        {
            showing = false;
            TextBoxPureLyric.FontWeight = FontWeights.Normal;
            TextBoxTranslation.FontWeight = FontWeights.Normal;
            TextBoxPureLyric.Foreground = new SolidColorBrush(Color.FromArgb(255,155,155,155));
            TextBoxTranslation.Foreground = new SolidColorBrush(Color.FromArgb(255, 155, 155, 155));
            TextBoxPureLyric.FontSize = actualsize;
            TextBoxTranslation.FontSize = actualsize;
        }
    }
}
