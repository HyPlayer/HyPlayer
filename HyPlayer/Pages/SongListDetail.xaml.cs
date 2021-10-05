#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SongListDetail : Page, IDisposable
    {
        private int page;
        private NCPlayList playList;
        public ObservableCollection<NCSong> Songs;


        public SongListDetail()
        {
            InitializeComponent();
            Songs = new ObservableCollection<NCSong>();
        }

        public void Dispose()
        {
            ImageRect.ImageSource = null;
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
            await Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    if (playList.plid != "-666")
                    {
                        try
                        {
                            SongsList.ListSource = "content";
                            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                                new Dictionary<string, object> { { "id", playList.plid } });
                            var trackIds = json["playlist"]["trackIds"].Select(t => (int)t["id"]).Skip(page * 500)
                                .Take(500)
                                .ToArray();

                            if (trackIds.Length >= 500)
                                NextPage.Visibility = Visibility.Visible;
                            else
                                NextPage.Visibility = Visibility.Collapsed;


                            if (json["playlist"]["specialType"].ToString() == "5" &&
                                json["playlist"]["userId"].ToString() == Common.LoginedUser.id)
                                Common.Invoke(() => { ButtonIntel.Visibility = Visibility.Visible; });
                            if (json["playlist"]["userId"].ToString() == Common.LoginedUser.id)
                                SongsList.IsMySongList = true;
                            try
                            {
                                json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                                    new Dictionary<string, object> { ["ids"] = string.Join(",", trackIds) });
                                var idx = page * 500;
                                var i = 0;
                                foreach (var jToken in json["songs"])
                                {
                                    var song = (JObject)jToken;

                                    var ncSong = NCSong.CreateFromJson(song);
                                    ncSong.IsAvailable =
                                        json["privileges"].ToList()[i++]["st"].ToString() == "0";
                                    ncSong.Order = idx++;
                                    Common.Invoke(() => { Songs.Add(ncSong); });
                                }
                            }

                            catch (Exception ex)
                            {
                                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                            }
                        }
                        catch (Exception ex)
                        {
                            Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                        }
                    }
                    else
                    {
                        Common.Invoke(() =>
                        {
                            //每日推荐
                            ButtonIntel.Visibility = Visibility.Collapsed;
                        });
                        try
                        {
                            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendSongs);
                            if (json["data"]["dailySongs"][0]["alg"].ToString() == "birthDaySong")
                                Common.Invoke(() =>
                                {
                                    // 诶呀,没想到还过生了,吼吼
                                    TextBlockDesc.Text = "生日快乐~ 今天也要开心哦!";
                                    TextBlockDesc.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
                                    TextBlockDesc.FontSize = 25;
                                });

                            var idx = 0;
                            foreach (var song in json["data"]["dailySongs"])
                            {
                                var ncSong = NCSong.CreateFromJson(song);
                                ncSong.IsAvailable = true;
                                ncSong.Order = idx++;
                                Common.Invoke(() => { Songs.Add(ncSong); });
                            }
                        }
                        catch (Exception ex)
                        {
                            Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                        }
                    }
                });
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SongsList.Dispose();
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
                                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                            }
                        }
                    }

                    SongsList.ListSource = "pl" + playList.plid;
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
                    if (playList.plid != "-666")
                    {
                        HyPlayList.RemoveAllSong();
                        HyPlayList.SongAppendDone();
                        await HyPlayList.AppendPlayList(playList.plid);
                        HyPlayList.SongAppendDone();
                        HyPlayList.NowPlaying = -1;
                        HyPlayList.SongMoveNext();
                    }
                    else
                    {
                        HyPlayList.AppendNcSongs(Songs.ToList());
                        HyPlayList.SongAppendDone();
                        HyPlayList.NowPlaying = -1;
                        HyPlayList.SongMoveNext();
                    }
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
                            new Dictionary<string, object>
                                { { "pid", playList.plid }, { "id", Songs[0].sid } /*, { "sid", Songs[0].sid }*/ });
                        var IntSongs = new List<NCSong>();
                        IntSongs.Add(Songs[new Random().Next(0, Songs.Count)]);
                        foreach (var token in jsona["data"])
                            try
                            {
                                if (token["songInfo"] != null)
                                {
                                    var ncSong = NCSong.CreateFromJson(token["songInfo"]);
                                    IntSongs.Add(ncSong);
                                }
                            }
                            catch
                            {
                                //ignore
                            }

                        try
                        {
                            HyPlayList.AppendNcSongs(IntSongs);

                            HyPlayList.SongAppendDone();

                            HyPlayList.SongMoveTo(0);
                        }
                        catch (Exception ex)
                        {
                            Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                    }
                });
            });
        }

        private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(Songs.ToList());
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
    }
}