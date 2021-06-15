using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Pages;
using AcrylicBackgroundSource = Windows.UI.Xaml.Media.AcrylicBackgroundSource;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleNCSong : UserControl
    {
        private NCSong ncsong;
        private readonly bool CanPlay;

        public SingleNCSong(NCSong song, int order, bool canplay = true)
        {
            InitializeComponent();
            ncsong = song;
            CanPlay = canplay;
            if (!CanPlay)
            {
                BtnPlay.Visibility = Visibility.Collapsed;
                TextBlockSongname.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            }

            ImageRect.Source =
                new BitmapImage(new Uri(song.Album.cover + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER));
            TextBlockSongname.Text = song.songname;
            TextBlockAlbum.Text = song.Album.name;
            OrderId.Text = (order + 1).ToString();
            TextBlockArtist.Text = string.Join(" / ", song.Artist.Select(ar => ar.name));
            if (song.mvid != 0)
            {
                BtnMV.IsEnabled = true;
            }
        }

        public async Task<bool> AppendMe()
        {
            if (!CanPlay)
            {
                return false;
            }

            await HyPlayList.AppendNCSong(ncsong);
            HyPlayList.SongAppendDone();
            //此处可以进行优化
            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.NcPlayItem.sid == ncsong.sid));
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

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            _ = AppendMe();
        }

        private void Grid1_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlAccentAcrylicElementAccentMediumHighBrush"] as Brush;
            _ = AppendMe();
        }

        private async void TextBlockArtist_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (ncsong.Artist.Count > 1)
            {
                await new ArtistSelectDialog(ncsong.Artist).ShowAsync();
            }
            else
            {
                Common.BaseFrame.Navigate(typeof(ArtistPage), ncsong.Artist[0].id);
            }
        }

        private void BtnDownload_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(ncsong);
        }

        private void Comments_Click(object sender, RoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(Comments), ncsong);
        }

        private void BtnMV_OnClick(object sender, RoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(MVPage), ncsong.mvid);
        }

        private async void Btn_Sub_OnClick(object sender, RoutedEventArgs e)
        {
            await new SongListSelect(ncsong.sid).ShowAsync();
        }

        private void TextBlockAlbum_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(AlbumPage), ncsong.Album);
        }
    }

}
