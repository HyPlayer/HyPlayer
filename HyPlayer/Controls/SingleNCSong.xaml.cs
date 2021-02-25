using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using Microsoft.UI.Xaml.Media;
using AcrylicBackgroundSource = Windows.UI.Xaml.Media.AcrylicBackgroundSource;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleNCSong : UserControl
    {
        private NCSong ncsong;
        public SingleNCSong(NCSong song)
        {
            this.InitializeComponent();
            ncsong = song;
            ImageRect.ImageSource = new BitmapImage(new Uri(song.Album.cover+ "?param="+StaticSource.PICSIZE_SINGLENCSONG_COVER));
            TextBlockSongname.Text = song.songname;
            TextBlockArtist.Text = string.Join(" / ", song.Artist.Select(ar => ar.name));
        }

        public async Task<bool> AppendMe()
        {
            await HyPlayList.AppendNCSong(ncsong);
            return true;
        }

        private void UIElement_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background = new Windows.UI.Xaml.Media.AcrylicBrush()
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintOpacity = 0.67500003206078,
                TintLuminosityOpacity = 0.183000008692034,
                TintColor = Windows.UI.Color.FromArgb(255, 0, 142, 230),
                FallbackColor = Windows.UI.Color.FromArgb(255, 0, 120, 210)
            };

        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = new Windows.UI.Xaml.Media.AcrylicBrush()
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop, TintOpacity = 0.67500003206078,
                TintLuminosityOpacity = 0.183000008692034, TintColor = Windows.UI.Color.FromArgb(255, 128, 128, 128),
                FallbackColor = Windows.UI.Color.FromArgb(255, 128, 128, 128)
            };
        }

        private void Grid1_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = null;
        }

        private void Grid1_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            Grid1.Background = new Windows.UI.Xaml.Media.AcrylicBrush()
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintOpacity = 0.67500003206078,
                TintLuminosityOpacity = 0.183000008692034,
                TintColor = Windows.UI.Color.FromArgb(10, 147, 205, 241),
                FallbackColor = Windows.UI.Color.FromArgb(10, 135, 206, 235)
            };
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            _ = AppendMe();
        }
    }
}
