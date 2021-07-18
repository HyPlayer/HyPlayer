using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using HyPlayer.Pages;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleArtist : UserControl
    {
        private readonly NCArtist Artist;

        public SingleArtist(NCArtist artist)
        {
            Artist = artist;
            InitializeComponent();
            TextBlockSingerName.Text = artist.name;
            TextBlockTransName.Text = artist.transname;
            TextBlockAlias.Text = artist.alias;
            if (artist.avatar != null)
                ImageRect.Source =
                    new BitmapImage(new Uri(artist.avatar + "?param=" + StaticSource.PICSIZE_SINGLENCSINGER_COVER));
        }

        private void UIElement_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintOpacity = 0.67500003206078,
                TintLuminosityOpacity = 0.183000008692034,
                TintColor = Color.FromArgb(255, 0, 142, 230),
                FallbackColor = Color.FromArgb(255, 54, 54, 210)
            };
        }

        private void Grid1_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlAccentAcrylicElementAccentMediumHighBrush"] as Brush;
            Common.BaseFrame.Navigate(typeof(ArtistPage), Artist.id);
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
    }
}