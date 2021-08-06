using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class SingleAlbum : UserControl
    {
        private readonly NCAlbum Album;
        private readonly List<NCArtist> Artists;

        public SingleAlbum(NCAlbum album, List<NCArtist> artists)
        {
            Album = album;
            Artists = artists;
            InitializeComponent();
            TextBlockAlbumName.Text = album.name;
            TextBlockArtistName.Text = string.Join(" / ", artists.Select(t => t.name));
            TextBlockAlias.Text = album.alias;
            ImageRect.Source =
                new BitmapImage(new Uri(album.cover + "?param=" + StaticSource.PICSIZE_SINGLENCALBUM_COVER));
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
            Common.NavigatePage(typeof(AlbumPage), Album);
        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = Application.Current.Resources["SystemControlAltLowAcrylicElementBrush"] as Brush;
            Grid1.BorderBrush =
                Application.Current.Resources["SystemControlBackgroundListMediumRevealBorderBrush"] as Brush;
        }

        private void Grid1_OnPointerExited(object sender, PointerRoutedEventArgs e) => SetUnfocusedState();

        private void Grid1_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => SetUnfocusedState();

        private void SetUnfocusedState()
        {
            Grid1.Background = null;
            Grid1.BorderBrush = new SolidColorBrush();
        }

        private void Grid1_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlChromeMediumAcrylicElementMediumBrush"] as Brush;
        }

        private async void TextBlockArtist_OnTapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            if (Artists.Count > 1)
                await new ArtistSelectDialog(Artists).ShowAsync();
            else
                Common.NavigatePage(typeof(ArtistPage), Artists[0].id);
        }
    }
}