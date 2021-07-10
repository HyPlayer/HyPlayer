using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Pages;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SinglePlaylistStack : UserControl
    {
        private NCPlayList Playlist;

        public SinglePlaylistStack(NCPlayList pl)
        {
            Playlist = pl;
            this.InitializeComponent();
            TextBlockPlaylistName.Text = Playlist.name;
            TextBlockUsername.Text = Playlist.creater.name;
            TextBlockDesc.Text = $"{Playlist.trackCount}首 , 播放{Playlist.playCount}次 , 收藏{Playlist.bookCount}次";
            ImageRect.Source =
                new BitmapImage(new Uri(Playlist.cover + "?param=" + StaticSource.PICSIZE_SINGLENCPLAYLIST_COVER));

        }

        private void UIElement_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background = new Windows.UI.Xaml.Media.AcrylicBrush()
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintOpacity = 0.67500003206078,
                TintLuminosityOpacity = 0.183000008692034,
                TintColor = Windows.UI.Color.FromArgb(255, 0, 142, 230),
                FallbackColor = Windows.UI.Color.FromArgb(255, 54, 54, 210)
            };
        }

        private void Grid1_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlAccentAcrylicElementAccentMediumHighBrush"] as Brush;
            Common.BaseFrame.Navigate(typeof(SongListDetail), Playlist);
        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = Application.Current.Resources["SystemControlAltLowAcrylicElementBrush"] as Brush;
            Grid1.BorderBrush =
                Application.Current.Resources["SystemControlBackgroundListMediumRevealBorderBrush"] as Brush;
        }

        private void Grid1_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = null;
            Grid1.BorderBrush = new SolidColorBrush();
        }

        private void Grid1_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlChromeMediumAcrylicElementMediumBrush"] as Brush;
        }

        private void TextBlockUsername_OnTapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            Common.BaseFrame.Navigate(typeof(Me),Playlist.creater.id);
        }
    }
}
