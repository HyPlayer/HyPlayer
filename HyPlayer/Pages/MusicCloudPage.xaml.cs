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
    public sealed partial class MusicCloudPage : Page, IDisposable
    {
        private int page;
        private ObservableCollection<NCSong> Items = new ObservableCollection<NCSong>();

        public MusicCloudPage()
        {
            InitializeComponent();
        }

        public async void LoadMusicCloudItem()
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserCloud,
                    new Dictionary<string, object>()
                    {
                        { "limit", 200 },
                        { "offset", page * 200 }
                    });
                foreach (var jToken in json["data"])
                {
                    var ret = NCSong.CreateFromJson(jToken["simpleSong"]);
                    if (ret.Artist[0].id == "0")
                    {
                        //不是标准歌曲
                        ret.Album.name = jToken["album"].ToString();
                        ret.Artist.Clear();
                        ret.Artist.Add(new NCArtist()
                        {
                            name = jToken["artist"].ToString()
                        });
                    }

                    Items.Add(ret);
                }

                NextPage.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("发生错误", ex.Message);
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
            await Task.Run(() => { Common.Invoke(() => { LoadMusicCloudItem(); }); });
        }


        private void SongContainer_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    try
                    {
                        await HyPlayList.AppendNCSongs(Items.ToList());
                        HyPlayList.SongAppendDone();
                        HyPlayList.SongMoveTo(SongContainer.SelectedIndex);
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
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
            DownloadManager.AddDownload(Items.ToList());
        }

        public void Dispose()
        {
            Items = null;
        }
    }
}