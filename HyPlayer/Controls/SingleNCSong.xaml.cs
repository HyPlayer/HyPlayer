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

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleNCSong : UserControl
    {
        public SingleNCSong(NCSong song)
        {
            this.InitializeComponent();
            ImageAlbum.Source = new BitmapImage(new Uri(song.Album.cover));
            TextBlockSongname.Text = song.songname;
            string artisttxt = "";
            song.artist.ForEach(s => { artisttxt += s.name; });
            TextBlockArtist.Text = artisttxt;
        }

        private void UIElement_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            
        }
    }
}
