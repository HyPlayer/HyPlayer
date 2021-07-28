using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MusicCloudPage : Page
    {
        private int page;
        private ObservableCollection<PanItemStruct> Items = new ObservableCollection<PanItemStruct>();

        public MusicCloudPage()
        {
            InitializeComponent();
        }

        public async void LoadMusicCloudItem()
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserCloud,
                new Dictionary<string, object>()
                {
                    {"limit", 200},
                    {"offset", page * 200}
                });
            if (isOk)
            {
                foreach (var jToken in json["data"])
                {
                    var ret = PanItemStruct.CreateFromJson(jToken);
                    Items.Add(ret);
                }

                NextPage.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SongContainer.ItemsSource = Items;
            await Task.Run(() => { Common.Invoke(async () => { LoadMusicCloudItem(); }); });
        }


        private void SongContainer_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    HyPlayList.RemoveAllSong();
                    var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                        new Dictionary<string, object>
                        {
                            {"id", string.Join(',', Items.Select(t => t.id))}
                        });
                    if (isok)
                    {
                        var arr = json["data"].ToList();
                        for (var i = 0; i < Items.Count; i++)
                        {
                            var token = arr.Find(jt => jt["id"].ToString() == Items[i].id);
                            if (!token.HasValues) continue;

                            var ncSong = Items[i];

                            var tag = "";
                            if (token["type"].ToString().ToLowerInvariant() == "flac")
                                tag = "SQ";
                            else
                                tag = ncSong.bitrate + "K";

                            var hpi = new HyPlayItem
                            {
                                AudioInfo = new AudioInfo
                                {
                                    SongName = ncSong.name,
                                    Artist = ncSong.artistname,
                                    ArtistArr = new string[]
                                    {
                                        ncSong.artistname
                                    },
                                    Album = ncSong.albumname,
                                    Lyric = null,
                                    TrLyric = null,
                                    LengthInMilliseconds = ncSong.duration,
                                    Picture = "https://p3.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg",
                                    liked = false,
                                    tag = tag,
                                    LocalSongFile = null
                                },
                                ItemType = HyPlayItemType.Pan,
                                Name = ncSong.name,
                                NcPlayItem = new NCPlayItem
                                {
                                    hasLocalFile = false,
                                    LocalStorageFile = null,
                                    bitrate = int.Parse(ncSong.bitrate),
                                    tag = tag,
                                    id = ncSong.id,
                                    songname = ncSong.name,
                                    Type = HyPlayItemType.Pan,
                                    Artist = new List<NCArtist>() {new NCArtist() {id = "0", name = ncSong.artistname}},
                                    Album = new NCAlbum(){id = "0",AlbumType = HyPlayItemType.Pan,name = ncSong.artistname,cover = "https://p3.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg"},
                                    url = token["url"].ToString(),
                                    subext = token["type"].ToString(),
                                    size = ncSong.size.ToString(),
                                    md5 = null,
                                    LengthInMilliseconds = ncSong.duration
                                },
                                Path = null
                            };
                            HyPlayList.List.Add(hpi);
                        }

                        HyPlayList.SongAppendDone();

                        HyPlayList.SongMoveTo(SongContainer.SelectedIndex);
                    }
                });
            });
        }


        private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            LoadMusicCloudItem();
        }

        private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(Common.ListedSongs);
        }
    }

    public class PanItemStruct
    {
        public string id;
        public long size;

        // public string coverid;
        // public string lyricid;
        public string albumname;
        public string artistname;
        public string bitrate;
        public string name;
        public double duration;

        public static PanItemStruct CreateFromJson(JToken json)
        {
            return new PanItemStruct
            {
                id = json["songId"].ToString(),
                size = json["fileSize"].ToObject<long>(),
                albumname = json["album"].ToString(),
                artistname = json["artist"].ToString(),
                bitrate = json["bitrate"].ToString(),
                name = json["songName"].ToString(),
                duration = json["simpleSong"]["dt"].ToObject<double>()
            };
        }
    }
}