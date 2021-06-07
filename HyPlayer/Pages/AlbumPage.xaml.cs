using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Input.Spatial;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class AlbumPage : Page
    {
        private int page = 0;
        private readonly List<NCSong> songs = new List<NCSong>();
        private NCAlbum Album;
        private List<NCArtist> artists = new List<NCArtist>();

        public AlbumPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await Task.Run((() =>
            {
                Common.Invoke(async () =>
                {
                    if (e.Parameter != null)
                    {
                        if (e.Parameter is NCAlbum)
                            Album = (NCAlbum) e.Parameter;
                        ImageRect.ImageSource =
                            new BitmapImage(
                                new Uri(Album.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
                        TextBoxAlbumName.Text = Album.name;
                        var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Album,
                            new Dictionary<string, object>() {{"id", Album.id}});
                        if (isok)
                        {
                            TextBoxAlbumName.Text = json["album"]["name"].ToString();
                            artists = json["album"]["artists"].ToArray().Select(t => new NCArtist()
                            {
                                avatar = t["picUrl"].ToString(),
                                id = t["id"].ToString(),
                                name = t["name"].ToString()
                            }).ToList();
                            TextBoxAuthor.Text = string.Join(" / ", artists.Select(t => t.name));
                            TextBlockDesc.Text = (json["album"]["alias"].HasValues
                                                     ? string.Join(" / ",
                                                           json["album"]["alias"].ToArray().Select(t => t.ToString())) +
                                                       "\r\n"
                                                     : "")
                                                 + json["album"]["description"].ToString();
                            string cdname = "";
                            StackPanel stp = null;
                            int idx = 0;
                            foreach (var song in json["songs"].ToArray())
                            {
                                NCSong NCSong = NCSong.CreateFromJson(song); 
                                songs.Add(NCSong);
                                if (song["cd"].ToString() != cdname)
                                {
                                    idx = 0;
                                    cdname = song["cd"].ToString();
                                    stp = new StackPanel();
                                    SongContainer.Children.Add(new StackPanel()
                                    {
                                        Orientation = Orientation.Vertical,
                                        Children =
                                        {
                                            new TextBlock()
                                            {
                                                Margin = new Thickness(5,0,0,0),
                                                FontWeight = FontWeights.Black, FontSize = 23, Text = "Disc " + cdname
                                            },
                                            stp
                                        }
                                    });
                                }

                                stp.Children.Add(new SingleNCSong(NCSong, idx++,song["privilege"]["st"].ToString()=="0"));
                            }
                        }
                    }
                });
            }));
        }


        private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    HyPlayList.RemoveAllSong();
                    (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                        new Dictionary<string, object>()
                            {{"id", string.Join(',', songs.Select(t => t.sid))}, {"br", Common.Setting.audioRate}});
                    if (isok)
                    {
                        List<JToken> arr = json["data"].ToList();
                        for (int i = 0; i < songs.Count; i++)
                        {
                            JToken token = arr.Find(jt => jt["id"].ToString() == songs[i].sid);
                            if (!token.HasValues)
                            {
                                continue;
                            }

                            NCSong ncSong = songs[i];

                            string tag = "";
                            if (token["type"].ToString().ToLowerInvariant() == "flac")
                            {
                                tag = "SQ";
                            }
                            else
                            {
                                tag = (token["br"].ToObject<int>() / 1000).ToString() + "k";
                            }

                            NCPlayItem ncp = new NCPlayItem()
                            {
                                tag = tag,
                                Album = ncSong.Album,
                                Artist = ncSong.Artist,
                                subext = token["type"].ToString(),
                                sid = ncSong.sid,
                                songname = ncSong.songname,
                                url = token["url"].ToString(),
                                LengthInMilliseconds = ncSong.LengthInMilliseconds,
                                size = token["size"].ToString(),
                                md5 = token["md5"].ToString()
                            };
                            HyPlayList.AppendNCPlayItem(ncp);
                        }

                        HyPlayList.SongAppendDone();

                        HyPlayList.SongMoveTo(0);
                    }
                }));
            }));
        }


        private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
        }

        private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(songs);
        }

        private async void TextBoxAuthor_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (artists.Count > 1)
            {
                await new ArtistSelectDialog(artists).ShowAsync();
            }
            else
            {
                Common.BaseFrame.Navigate(typeof(ArtistPage), artists[0].id);
            }
        }
    }
}