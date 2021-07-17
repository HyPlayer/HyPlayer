using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;

namespace HyPlayer.Pages
{
    public sealed partial class RadioPage : Page
    {
        private NCRadio Radio;
        private int page = 0;
        private bool asc = false;
        int i = 0;

        public RadioPage()
        {
            InitializeComponent();
        }

        private async void LoadProgram()
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjProgram,
                new Dictionary<string, object>()
                {
                    { "rid", Radio.id },
                    { "offset", page * 30 },
                    { "asc", asc }
                });
            if (isok)
            {
                NextPage.Visibility = json["more"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var jToken in json["programs"])
                {
                    var song = NCFmItem.CreateFromJson(jToken);
                    Common.ListedSongs.Add(song);
                    SongContainer.Children.Add(new SingleNCSong(song, i++, true, true));
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is NCRadio radio)
            {
                Radio = radio;
                TextBoxRadioName.Text = radio.name;
                TextBoxDJ.Text = radio.DJ.name;
                TextBlockDesc.Text = radio.desc;
                ImageRect.ImageSource =
                    new BitmapImage(new Uri(radio.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
                Common.ListedSongs.Clear();
                LoadProgram();
            }
        }

        private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            LoadProgram();
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
                        {
                            { "id", string.Join(',', Common.ListedSongs.Select(t => t.sid)) },
                            { "br", Common.Setting.audioRate }
                        });
                    if (isok)
                    {
                        List<JToken> arr = json["data"].ToList();
                        for (int i = 0; i < Common.ListedSongs.Count; i++)
                        {
                            JToken token = arr.Find(jt => jt["id"].ToString() == Common.ListedSongs[i].sid);
                            if (!token.HasValues)
                            {
                                continue;
                            }

                            NCSong ncSong = Common.ListedSongs[i];

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
                                id = ncSong.sid,
                                Type = HyPlayItemType.Radio,
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

        private void TextBoxDJ_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(Me), Radio.DJ.id);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            SongContainer.Children.Clear();
            page = 0;
            asc = !asc;
            LoadProgram();
        }
    }
}