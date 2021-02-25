using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Search : Page
    {
        int page = 0;
        public Search()
        {
            this.InitializeComponent();
        }

        private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            page = 0;
            LoadResult();
        }

        private async void LoadResult()
        {
            SearchResultContainer.Children.Clear();
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Cloudsearch, new Dictionary<string, object>() { { "keywords", TextBoxSearchText.Text},{"offset",page*30}});
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
                        LengthInMilliseconds = Double.Parse(song["dt"].ToString())
                    };
                    song["ar"].ToList().ForEach(t =>
                    {
                        NCSong.Artist.Add(new NCArtist()
                        {
                            id = t["id"].ToString(),
                            name = t["name"].ToString()
                        });
                    });
                    SearchResultContainer.Children.Add(new SingleNCSong(NCSong, idx++, song["privilege"]["st"].ToString() == "0"));
                }

                if (int.Parse(json["result"]["songCount"].ToString()) >= (page+1) * 30)
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
