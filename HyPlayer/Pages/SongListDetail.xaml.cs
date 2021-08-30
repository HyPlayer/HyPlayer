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
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using Windows.UI.Xaml.Media;
using Windows.UI;
using HyPlayer.Controls;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SongListDetail : Page, IDisposable
    {
        private string intelsong = "";
        private int page;
        private NCPlayList playList;
        private bool IsManualSelect = true;
        public List<NCSong> Songs;

        public SongListDetail()
        {
            InitializeComponent();
            Songs = new List<NCSong>();
        }

        public void LoadSongListDetail()
        {
            if (playList.cover.StartsWith("http"))
                ImageRect.ImageSource =
                    new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
            else
                ImageRect.ImageSource =
                    new BitmapImage(new Uri(playList.cover));


            TextBoxPLName.Text = playList.name;
            TextBlockDesc.Text = playList.desc;
            TextBoxAuthor.Text = playList.creater.name;
            ToggleButtonLike.IsChecked = playList.subscribed;
            if (playList.creater.id == Common.LoginedUser.id)
                Edit.Visibility = Visibility.Visible;
        }

        public async void LoadSongListItem()
        {
            await Task.Run(async () =>
            {
                if (playList.plid != "-666")
                {
                    try
                    {

                        var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                        new Dictionary<string, object> { { "id", playList.plid } });
                        var trackIds = json["playlist"]["trackIds"].Select(t => (int)t["id"]).Skip(page * 500).Take(500)
                            .ToArray();
                        Common.Invoke(() =>
                        {
                            if (trackIds.Length >= 500)
                                NextPage.Visibility = Visibility.Visible;
                            else
                                NextPage.Visibility = Visibility.Collapsed;
                        });

                        if (json["playlist"]["specialType"].ToString() == "5")
                            Common.Invoke(() => { ButtonIntel.Visibility = Visibility.Visible; });
                        try
                        {
                            json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                                new Dictionary<string, object> { ["ids"] = string.Join(",", trackIds) });
                            Common.ListedSongs = new List<NCSong>();
                            var idx = page * 500;
                            var i = 0;
                            foreach (var jToken in json["songs"])
                            {
                                var song = (JObject)jToken;
                                if (string.IsNullOrEmpty(intelsong)) intelsong = song["id"].ToString();

                                var ncSong = NCSong.CreateFromJson(song);
                                ncSong.IsAvailable =
                                    json["privileges"].ToList()[i++]["st"].ToString() == "0";
                                ncSong.Order = idx++;
                                if (ncSong.IsAvailable) Common.ListedSongs.Add(ncSong);
                                Songs.Add(ncSong);
                            }
                        }

                        catch (Exception ex)
                        {
                            Common.ShowTeachingTip("发生错误", ex.Message);
                        }

                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
                    }
                }
                else
                {
                    Common.Invoke(() =>
                    { //每日推荐
                        ButtonIntel.Visibility = Visibility.Collapsed;
                    });
                    try
                    {
                        var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendSongs);
                        if (json["data"]["dailySongs"][0]["alg"].ToString() == "birthDaySong")
                        {
                            Common.Invoke(() =>
                            {
                                // 诶呀,没想到还过生了,吼吼
                                TextBlockDesc.Text = "生日快乐~ 今天也要开心哦!";
                                TextBlockDesc.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
                                TextBlockDesc.FontSize = 25;
                            });
                        }

                        var idx = 0;
                        foreach (var song in json["data"]["dailySongs"])
                        {
                            var ncSong = NCSong.CreateFromJson(song);
                            ncSong.IsAvailable = true;
                            ncSong.Order = idx++;
                            if (ncSong.IsAvailable) Common.ListedSongs.Add(ncSong);
                            Songs.Add(ncSong);

                        }
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
                    }
                }
                Common.Invoke(() =>
                {
                    if (SongContainer.ItemsSource == null)
                        SongContainer.ItemsSource = Songs;
                });
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ImageRect.ImageSource = null;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    try
                    {
                        var anim = ConnectedAnimationService.GetForCurrentView()
                            .GetAnimation("SongListExpand");
                        var anim1 = ConnectedAnimationService.GetForCurrentView()
                            .GetAnimation("SongListExpandAcrylic");
                        anim1?.TryStart(GridPersonalInformation);
                        anim?.TryStart(RectangleImage);
                    }
                    catch
                    {
                    }
                });
            });
            await Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    if (e.Parameter != null)
                    {
                        if (e.Parameter is NCPlayList)
                        {
                            playList = (NCPlayList)e.Parameter;
                        }
                        else
                        {
                            var pid = e.Parameter.ToString();

                            try
                            {
                                var json = await Common.ncapi.RequestAsync(
                                    CloudMusicApiProviders.PlaylistDetail,
                                    new Dictionary<string, object> { { "id", pid } });
                                playList = NCPlayList.CreateFromJson(json["playlist"]);
                            }
                            catch (Exception ex)
                            {
                                Common.ShowTeachingTip("发生错误", ex.Message);
                            }
                        }
                    }

                    LoadSongListDetail();
                    LoadSongListItem();
                });
            });
        }


        private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    HyPlayList.List.Clear();
                    HyPlayList.SongAppendDone();
                    await HyPlayList.AppendPlayList(playList.plid);
                    HyPlayList.SongAppendDone();
                    HyPlayList.NowPlaying = -1;
                    HyPlayList.SongMoveNext();
                });
            });
        }


        private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            LoadSongListItem();
        }

        private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(Comments), "pl" + playList.plid);
        }

        private void ButtonHeartBeat_OnClick(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    HyPlayList.RemoveAllSong();
                    try
                    {
                        var jsona = await Common.ncapi.RequestAsync(
                            CloudMusicApiProviders.PlaymodeIntelligenceList,
                            new Dictionary<string, object> { { "pid", playList.plid }, { "id", intelsong } });
                        var Songs = new List<NCSong>();
                        foreach (var token in jsona["data"])
                        {
                            var ncSong = NCSong.CreateFromJson(token["songInfo"]);
                            Songs.Add(ncSong);
                        }

                        try
                        {
                            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                                new Dictionary<string, object>
                                {
                                    { "id", string.Join(",", Songs.Select(t => t.sid)) },
                                    { "br", Common.Setting.audioRate }
                                });

                            var arr = json["data"].ToList();
                            for (var i = 0; i < Songs.Count; i++)
                            {
                                var token = arr.Find(jt => jt["id"].ToString() == Songs[i].sid);
                                if (!token.HasValues) continue;

                                var ncSong = Songs[i];

                                var tag = "";
                                if (token["type"].ToString().ToLowerInvariant() == "flac")
                                    tag = "SQ";
                                else
                                    tag = token["br"].ToObject<int>() / 1000 + "k";

                                var ncp = new PlayItem
                                {
                                    tag = tag,
                                    Album = ncSong.Album,
                                    Artist = ncSong.Artist,
                                    subext = token["type"].ToString(),
                                    Type = HyPlayItemType.Netease,
                                    id = ncSong.sid,
                                    Name = ncSong.songname,
                                    url = token["url"].ToString(),
                                    LengthInMilliseconds = ncSong.LengthInMilliseconds,
                                    size = token["size"].ToString(),
                                    //md5 = token["md5"].ToString()
                                };
                                HyPlayList.AppendNCPlayItem(ncp);
                            }

                            HyPlayList.SongAppendDone();

                            HyPlayList.SongMoveTo(0);
                        }
                        catch (Exception ex)
                        {
                            Common.ShowTeachingTip("发生错误", ex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
                    }
                });
            });
        }

        private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(Common.ListedSongs);
        }

        private void LikeBtnClick(object sender, RoutedEventArgs e)
        {
            _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistSubscribe,
                new Dictionary<string, object> { { "id", playList.plid }, { "t", playList.subscribed ? "0" : "1" } });
            playList.subscribed = !playList.subscribed;
        }

        private void TextBoxAuthor_Tapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            Common.NavigatePage(typeof(Me), playList.creater.id);
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout(TextBlockDesc);
        }

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDescUpdate,
                new Dictionary<string, object> { { "id", playList.plid }, { "desc", NewDesc.Text } });
            playList.desc = NewDesc.Text;
            LoadSongListDetail();
        }

        public void Dispose()
        {
            ImageRect.ImageSource = null;
        }

        private void SongContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsManualSelect) _ = Songs[SongContainer.SelectedIndex].AppendMe();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            _ = Songs[int.Parse((sender as Button).Tag.ToString())].AppendMe();
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse((sender as Button).Tag.ToString());
            (((sender as Button).Parent as StackPanel).Parent as Grid).ContextFlyout.ShowAt(sender as Button);
            IsManualSelect = true;
        }

        private async void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
        {
            var ncsong = Songs[SongContainer.SelectedIndex];
            var oldLoadList = ncsong.LoadList;
            ncsong.LoadList = false;
            await ncsong.AppendMe();
            ncsong.LoadList = oldLoadList;
        }

        private async void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
        {
            _ = await HyPlayList.AppendNCSong(Songs[SongContainer.SelectedIndex], HyPlayList.NowPlaying + 1);
            HyPlayList.SongAppendDone();
        }

        private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
        {
            if (Songs[SongContainer.SelectedIndex].Artist[0].Type == HyPlayItemType.Radio)
            {
                Common.NavigatePage(typeof(Me), Songs[SongContainer.SelectedIndex].Artist[0].id);
            }
            else
            {
                if (Songs[SongContainer.SelectedIndex].Artist.Count > 1)
                    await new ArtistSelectDialog(Songs[SongContainer.SelectedIndex].Artist).ShowAsync();
                else
                    Common.NavigatePage(typeof(ArtistPage), Songs[SongContainer.SelectedIndex].Artist[0].id);
            }
        }

        private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(AlbumPage), Songs[SongContainer.SelectedIndex].Album);
        }

        private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(Comments), "sg" + Songs[SongContainer.SelectedIndex].sid);
        }

        private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(Songs[SongContainer.SelectedIndex]);
        }

        private void BtnMV_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(MVPage), Songs[SongContainer.SelectedIndex]);
        }

        private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
        {
            await new SongListSelect(Songs[SongContainer.SelectedIndex].sid).ShowAsync();
        }

        private async void Btn_Del_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(async () =>
            {
                await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>()
            {
                { "op" , "del" },
                {"pid",playList.plid },
                {"tracks" , Songs[SongContainer.SelectedIndex].sid }
            });
            });
        }

        private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var element = sender as Grid;
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse(element.Tag.ToString());
            element.ContextFlyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
            IsManualSelect = true;
        }
    }
}