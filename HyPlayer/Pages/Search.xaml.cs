using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Search : Page
    {
        private int page = 0;
        private string Text = "";
        public Search()
        {
            InitializeComponent();
        }

        private async void LoadResult()
        {
            SearchResultContainer.Children.Clear();
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Cloudsearch, new Dictionary<string, object>() { { "keywords", Text }, { "offset", page * 30 } });
            if (isOk)
            {
                int idx = 0;
                foreach (JToken song in json["result"]["songs"].ToArray())
                {
                    NCSong NCSong = new NCSong()
                    {
                        Album = new NCAlbum()
                        {
                            cover = song["al"]["picUrl"].ToString(),
                            id = song["al"]["id"].ToString(),
                            name = song["al"]["name"].ToString()
                        },
                        sid = song["id"].ToString(),
                        songname = song["name"].ToString(),
                        Artist = new List<NCArtist>(),
                        LengthInMilliseconds = double.Parse(song["dt"].ToString())
                    };
                    song["ar"]?.ToList().ForEach(t =>
                    {
                        NCSong.Artist.Add(new NCArtist()
                        {
                            id = t["id"].ToString(),
                            name = t["name"].ToString(),
                        });
                    });
                    SearchResultContainer.Children.Add(new SingleNCSong(NCSong, idx++, song["privilege"]["st"].ToString() == "0"));
                }

                if (int.Parse(json["result"]["songCount"].ToString()) >= (page + 1) * 30)
                {
                    NextPage.Visibility = Visibility.Visible;
                }
                else
                {
                    NextPage.Visibility = Visibility.Collapsed;
                }
                if (page > 0)
                {
                    PrevPage.Visibility = Visibility.Visible;
                }
                else
                {
                    PrevPage.Visibility = Visibility.Collapsed;
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Text = (string) e.Parameter;
            LoadResult();
            bool IsFDOn = Common.Setting.FDOption;
            if (IsFDOn)
                this.Background = Application.Current.Resources["SystemControlAcrylicWindowBrush"] as Brush;
            else this.Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
        }

        private void PrevPage_OnClick(object sender, RoutedEventArgs e)
        {
            page--;
            LoadResult();
        }

        private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            LoadResult();
        }
    }
}
