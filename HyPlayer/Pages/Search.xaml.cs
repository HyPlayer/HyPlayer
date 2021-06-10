﻿using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
            NavigationViewSelector.SelectedItem = NavigationViewSelector.MenuItems[0];
        }

        private async void LoadResult()
        {
            SearchResultContainer.Children.Clear();
            var (isOk,json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Cloudsearch,
                new Dictionary<string, object>()
                {
                    {"keywords", Text},
                    {"type", ((NavigationViewItem) NavigationViewSelector.SelectedItem).Tag.ToString()},
                    { "offset", page * 30 }
                });
            if (isOk)
            {
                switch (((NavigationViewItem) NavigationViewSelector.SelectedItem).Tag.ToString())
                {
                    case "1":
                        LoadSongResult(json);
                        break;
                    case "10":
                        LoadAlbumResult(json);
                        break;
                    case "100":
                        LoadArtistResult(json);
                        break;
                }
            }
        }

        private void LoadArtistResult(JObject json)
        {
            foreach (var singerjson in json["result"]["artists"].ToArray())
            {
                SearchResultContainer.Children.Add(new SingleArtist(NCArtist.CreateFormJson(singerjson)));
            }
            if (int.Parse(json["result"]["artistCount"].ToString()) >= (page + 1) * 30)
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

        private void LoadAlbumResult(JObject json)
        {
            foreach (var albumjson in json["result"]["albums"].ToArray())
            {
                SearchResultContainer.Children.Add(new SingleAlbum(NCAlbum.CreateFormJson(albumjson), albumjson["artists"].ToArray().Select(t=>NCArtist.CreateFormJson(t)).ToList()));
            }
            if (int.Parse(json["result"]["albumCount"].ToString()) >= (page + 1) * 30)
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
        
        private void LoadSongResult(JObject json)
        {
            int idx = 0;
            foreach (JToken song in json["result"]["songs"].ToArray())
            {
                NCSong NCSong = NCSong.CreateFromJson(song);
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
        
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Text = (string) e.Parameter;
            LoadResult();
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

        private void NavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            page = 0;
            LoadResult();
        }
    }
}
