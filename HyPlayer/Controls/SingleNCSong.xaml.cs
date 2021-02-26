﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
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
        private bool CanPlay;
        public SingleNCSong(NCSong song,int order,bool canplay=true)
        {
            this.InitializeComponent();
            ncsong = song;
            CanPlay = canplay;
            if (!CanPlay) {BtnPlay.Visibility = Visibility.Collapsed;
                TextBlockSongname.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            }
            ImageRect.ImageSource = new BitmapImage(new Uri(song.Album.cover+ "?param="+StaticSource.PICSIZE_SINGLENCSONG_COVER));
            TextBlockSongname.Text = song.songname;
            TextBlockAlbum.Text = song.Album.name;
            OrderId.Text = order.ToString();
            TextBlockArtist.Text = string.Join(" / ", song.Artist.Select(ar => ar.name));
        }

        public async Task<bool> AppendMe()
        {
            if (!CanPlay) return false;
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
                FallbackColor = Windows.UI.Color.FromArgb(255, 54, 54, 210)
            };

        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = new Windows.UI.Xaml.Media.AcrylicBrush()
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop, TintOpacity = 0.67500003206078,
                TintLuminosityOpacity = 0.183000008692034, TintColor = Windows.UI.Color.FromArgb(255, 54, 54, 54),
                FallbackColor = Windows.UI.Color.FromArgb(255, 54, 54, 54)
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

        private void Grid1_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            _ = AppendMe();
        }
    }
}
