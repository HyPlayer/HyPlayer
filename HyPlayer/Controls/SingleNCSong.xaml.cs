using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using Windows.UI.Xaml.Controls.Primitives;
using TagLib.Asf;
using Windows.System.Profile;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleNCSong : UserControl, IDisposable
    {
        private readonly bool CanPlay;
        private bool LoadList;
        public NCSong ncsong;
        public readonly string plId;

        public SingleNCSong(NCSong song, int order, bool canplay = true, bool loadlist = false,
            string additionalInfo = null, string songlistId = null)
        {
            InitializeComponent();
            ncsong = song;
            CanPlay = canplay;
            LoadList = loadlist;
            if (!CanPlay)
            {
                BtnPlay.Visibility = Visibility.Collapsed;
                TextBlockSongname.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            }
            if (AnalyticsInfo.VersionInfo.DeviceFamily != "Windows.Xbox")
                ImageRect.Source =
                new BitmapImage(new Uri(song.Album.cover + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER));
            TextBlockSongname.Text = song.songname;
            TextBlockTransName.Text = string.IsNullOrEmpty(song.transname) ? "" : $"({song.transname})";
            TextBlockAlia.Text = additionalInfo == null ? song.alias ?? "" : additionalInfo;
            TextBlockAlbum.Text = song.Album.name;
            FlyoutItemAlbum.Text = "专辑: " + TextBlockAlbum.Text;
            OrderId.Text = (order + 1).ToString();
            TextBlockArtist.Text = string.Join(" / ", song.Artist.Select(ar => ar.name));
            FlyoutItemSinger.Text = "歌手: " + TextBlockArtist.Text;
            //if (song.mvid != 0) BtnMV.IsEnabled = true;
            if (string.IsNullOrEmpty(songlistId))
            {
                Btn_Del.Visibility = Visibility.Collapsed;
            }
            else
            {
                plId = songlistId;
            }
        }

        [Obsolete]
        public async Task<bool> AppendMe()
        {
            if (!CanPlay) return false;
            // 
            var item = await HyPlayList.AppendNCSong(ncsong);
            //此处可以进行优化
            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.id == ncsong.sid));
            HyPlayList.SongAppendDone();

            return true;
        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = Application.Current.Resources["SystemControlAltLowAcrylicElementBrush"] as Brush;
            Grid1.BorderBrush =
                Application.Current.Resources["SystemControlBackgroundListMediumRevealBorderBrush"] as Brush;
        }

        private void Grid1_OnPointerExited(object sender, PointerRoutedEventArgs e) => SetUnfocusedState();

        // When scrolling in the collection control on a touchscreen-equipped device with pointer
        // inside the control, PointerCaptureLost event instead of PointerExited event gets triggered.
        // Handling both of the events would fix the styling issue in this situation.
        private void Grid1_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => SetUnfocusedState();

        private void SetUnfocusedState()
        {
            Grid1.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
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

        private async void BtnPlayNow_Click(object sender, RoutedEventArgs e)
        {
            var oldLoadList = LoadList;
            LoadList = false;
            await AppendMe();
            LoadList = oldLoadList;
        }

        private void Grid1_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlAccentAcrylicElementAccentMediumHighBrush"] as Brush;
            _ = AppendMe();
        }

        private async void TextBlockArtist_OnTapped(object sender, RoutedEventArgs routedEventArgs)
        {
            if (ncsong.Artist[0].Type == HyPlayItemType.Radio)
            {
                Common.NavigatePage(typeof(Me), ncsong.Artist[0].id);
            }
            else
            {
                if (ncsong.Artist.Count > 1)
                    await new ArtistSelectDialog(ncsong.Artist).ShowAsync();
                else
                    Common.NavigatePage(typeof(ArtistPage), ncsong.Artist[0].id);
            }
        }

        private void BtnDownload_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(ncsong);
        }

        private void Comments_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(Comments), "sg" + ncsong.sid);
        }

        private void BtnMV_OnClick(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(MVPage), ncsong);
        }

        private async void Btn_Sub_OnClick(object sender, RoutedEventArgs e)
        {
            await new SongListSelect(ncsong.sid).ShowAsync();
        }

        private void TextBlockAlbum_OnTapped(object sender, RoutedEventArgs routedEventArgs)
        {
            Common.NavigatePage(typeof(AlbumPage), ncsong.Album);
        }

        private async void Btn_Del_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>()
            {
                { "op" , "del" },
                {"pid",plId },
                {"tracks" , ncsong.sid }
            });
            Common.NavigateRefresh();
        }


        private void More_Click(object sender, RoutedEventArgs e)
        {
            Grid1.ContextFlyout.ShowAt(sender as Button);
        }

        private async void PlayNext_Click(object sender, RoutedEventArgs e)
        {
            _ = await HyPlayList.AppendNCSong(ncsong, HyPlayList.NowPlaying + 1);
            HyPlayList.SongAppendDone();
            //HyPlayList.SongMoveNext();
        }

        public void Dispose()
        {
            ncsong = null;
            ImageRect.Source = null;
        }
    }
}