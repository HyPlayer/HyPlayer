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

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleNCSong : UserControl
    {
        private readonly bool CanPlay;
        private readonly bool LoadList;
        public readonly NCSong ncsong;
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

        public async Task<bool> AppendMe()
        {
            if (!CanPlay) return false;

            if (LoadList)
            {
                _ = Task.Run(() =>
                {
                    Common.Invoke(async () =>
                    {
                        HyPlayList.RemoveAllSong();
                        var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                            new Dictionary<string, object>
                            {
                                {"id", string.Join(',', Common.ListedSongs.Select(t => t.sid))},
                                {"br", Common.Setting.audioRate}
                            });
                        if (isok)
                        {
                            var arr = json["data"].ToList();
                            for (var i = 0; i < Common.ListedSongs.Count; i++)
                            {
                                var token = arr.Find(jt => jt["id"].ToString() == Common.ListedSongs[i].sid);
                                if (!token.HasValues) continue;

                                var ncSong = Common.ListedSongs[i];
                                var tag = "";
                                if (token["type"].ToString().ToLowerInvariant() == "flac")
                                    tag = "SQ";
                                else
                                    tag = token["br"].ToObject<int>() / 1000 + "k";

                                var ncp = new NCPlayItem
                                {
                                    Type = ncSong.Type,
                                    tag = tag,
                                    Album = ncSong.Album,
                                    Artist = ncSong.Artist,
                                    subext = token["type"].ToString(),
                                    id = ncSong.sid,
                                    songname = ncSong.songname,
                                    url = token["url"].ToString(),
                                    LengthInMilliseconds = ncSong.LengthInMilliseconds,
                                    size = token["size"].ToString(),
                                    md5 = token["md5"].ToString()
                                };
                                var item = HyPlayList.AppendNCPlayItem(ncp);
                            }

                            HyPlayList.SongAppendDone();
                            //此处可以进行优化
                            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.NcPlayItem.id == ncsong.sid));
                        }
                    });
                });
            }
            else
            {
                var item = await HyPlayList.AppendNCSong(ncsong);
                HyPlayList.SongAppendDone();
                //此处可以进行优化
                HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.NcPlayItem.id == ncsong.sid));
            }

            return true;
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
            var ret = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>()
            {
                { "op" , "del" },
                {"pid",plId },
                {"tracks" , ncsong.sid }
            });
            Common.NavigateRefresh();
        }
    }
}