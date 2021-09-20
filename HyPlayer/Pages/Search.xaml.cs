#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Search : Page, IDisposable
    {
        private bool NoResult;
        private int page;
        private readonly ObservableCollection<NCSong> SongResults;
        private string Text = "";


        public Search()
        {
            InitializeComponent();
            NavigationViewSelector.SelectedItem = NavigationViewSelector.MenuItems[0];
            SongResults = new ObservableCollection<NCSong>();
            NavigationCacheMode = NavigationCacheMode.Required;
        }

        public void Dispose()
        {
            SearchResultContainer.ListItems.Clear();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var list = HistoryManagement.GetSearchHistory();
            foreach (var item in list)
            {
                var btn = new Button
                {
                    Content = item
                };
                btn.Click += Btn_Click;
                SearchHistory.Children.Add(btn);
            }

            if (Text != string.Empty) LoadResult();
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            SearchResultContainer.ListItems.Clear();
            GC.Collect();
        }

        private async void LoadResult()
        {
            if (string.IsNullOrEmpty(Text)) return;
            if (Convert.ToBase64String(Text.ToByteArrayUtf8()) == "6Ieq5p2A")
            {
                _ = Launcher.LaunchUriAsync(new Uri(@"http://music.163.com/m/topic/18926801"));
                return;
            }

            NoResult = false;
            HistoryManagement.AddSearchHistory(Text);
            var list = HistoryManagement.GetSearchHistory();
            SearchHistory.Children.Clear();
            foreach (var item in list)
            {
                var btn = new Button
                {
                    Content = item
                };
                btn.Click += Btn_Click;
                SearchHistory.Children.Add(btn);
            }

            SearchResultContainer.ListItems.Clear();
            SongResults.Clear();
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Cloudsearch,
                    new Dictionary<string, object>
                    {
                        { "keywords", Text },
                        { "type", ((NavigationViewItem)NavigationViewSelector.SelectedItem).Tag.ToString() },
                        { "offset", page * 30 }
                    });

                switch (((NavigationViewItem)NavigationViewSelector.SelectedItem).Tag.ToString())
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
                    case "1000":
                        LoadPlaylistResult(json);
                        break;
                    case "1009":
                        LoadRadioResult(json);
                        break;
                    case "1002":
                        LoadUserResult(json);
                        break;
                    case "1006":
                        LoadLyricResult(json);
                        break;
                }
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        private void LoadLyricResult(JObject json)
        {
            var i = 0;
            if (json["result"]["songCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            foreach (var songJs in json["result"]["songs"].ToArray())
                SearchResultContainer.ListItems.Add(
                    new SimpleListItem
                    {
                        Title = songJs["name"].ToString(),
                        LineOne = string.Join(" / ", songJs["ar"].Select(t => t["name"].ToString())),
                        LineTwo = songJs["lyrics"].ToList().First(t => t.ToString().Contains("</b>")).ToString(),
                        LineThree = string.Join("\r\n", songJs["lyrics"].ToList()),
                        ResourceId = "ns" + songJs["id"],
                        CoverUri = songJs["al"]["picUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                        Order = i++
                    });
            if (json["result"]["songCount"].ToObject<int>() >= (page + 1) * 30)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }

        private void LoadUserResult(JObject json)
        {
            var i = 0;
            if (json["result"]["userprofileCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            foreach (var userJs in json["result"]["userprofiles"].ToArray())
                SearchResultContainer.ListItems.Add(
                    new SimpleListItem
                    {
                        Title = userJs["nickname"].ToString(),
                        LineOne = userJs["signature"].ToString(),
                        LineTwo = "",
                        LineThree = "",
                        ResourceId = "us" + userJs["userId"],
                        CoverUri = userJs["avatarUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                        Order = i++
                    });
            if (json["result"]["userprofileCount"].ToObject<int>() >= (page + 1) * 30)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }

        private void LoadRadioResult(JObject json)
        {
            var i = 0;
            if (json["result"]["djRadiosCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            foreach (var pljs in json["result"]["djRadios"].ToArray())
                SearchResultContainer.ListItems.Add(
                    new SimpleListItem
                    {
                        Title = pljs["name"].ToString(),
                        LineOne = pljs["dj"]["nickname"].ToString(),
                        LineTwo = pljs["desc"].ToString(),
                        LineThree = pljs["rcmdText"].ToString(),
                        ResourceId = "rd" + pljs["id"],
                        CoverUri = pljs["picUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                        Order = i++
                    });
            if (json["result"]["djRadiosCount"].ToObject<int>() >= (page + 1) * 30)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }

        private void Btn_Click(object sender, RoutedEventArgs e)
        {
            Text = (sender as Button).Content.ToString();
            SearchKeywordBox.Text = Text;
            LoadResult();
        }

        private void LoadPlaylistResult(JObject json)
        {
            var i = 0;
            if (json["result"]["playlistCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            foreach (var pljs in json["result"]["playlists"].ToArray())
                SearchResultContainer.ListItems.Add(new SimpleListItem
                {
                    Title = pljs["name"].ToString(),
                    LineOne = pljs["creator"]["nickname"].ToString(),
                    LineTwo = pljs["description"].ToString(),
                    LineThree = $"{pljs["trackCount"]}首 | 播放{pljs["playCount"]}次 | 收藏 {pljs["bookCount"]}次",
                    ResourceId = "pl" + pljs["id"],
                    CoverUri = pljs["coverImgUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                    Order = i++
                });
            if (json["result"]["playlistCount"].ToObject<int>() >= (page + 1) * 30)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }

        private void LoadArtistResult(JObject json)
        {
            var i = 0;
            if (json["result"]["artistCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            foreach (var singerjson in json["result"]["artists"].ToArray())
                SearchResultContainer.ListItems.Add(new SimpleListItem
                {
                    Title = singerjson["name"].ToString(),
                    LineOne = singerjson["trans"].ToString(),
                    LineTwo = string.Join(" / ", singerjson["alia"].ToList()),
                    LineThree = $"专辑数 {singerjson["albumSize"]} | MV 数 {singerjson["mvSize"]}",
                    ResourceId = "ar" + singerjson["id"],
                    CoverUri = singerjson["img1v1Url"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                    Order = i++
                });
            if (json["result"]["artistCount"].ToObject<int>() >= (page + 1) * 30)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }

        private void LoadAlbumResult(JObject json)
        {
            var i = 0;
            if (json["result"]["albumCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            foreach (var albumjson in json["result"]["albums"].ToArray())
                SearchResultContainer.ListItems.Add(new SimpleListItem
                {
                    Title = albumjson["name"].ToString(),
                    LineOne = albumjson["artist"]["name"].ToString(),
                    LineTwo = albumjson["alias"] != null
                        ? string.Join(" / ", albumjson["alias"].ToArray().Select(t => t.ToString()))
                        : "",
                    LineThree = albumjson.Value<bool>("paid") ? "付费专辑" : "",
                    ResourceId = "al" + albumjson["id"],
                    CoverUri = albumjson["picUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                    Order = i++
                });
            if (json["result"]["albumCount"].ToObject<int>() >= (page + 1) * 30)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }

        private void LoadSongResult(JObject json)
        {
            if (json["result"]["songCount"].ToObject<int>() == 0)
            {
                NoResult = true;
                return;
            }

            var idx = 0;
            try
            {
                foreach (var song in json["result"]["songs"].ToArray())
                {
                    var ncSong = NCSong.CreateFromJson(song);
                    ncSong.Order = idx++;
                    SongResults.Add(ncSong);
                }

                if (json["result"]["songCount"].ToObject<int>() >= (page + 1) * 30)
                    NextPage.Visibility = Visibility.Visible;
                else
                    NextPage.Visibility = Visibility.Collapsed;
                if (page > 0)
                    PrevPage.Visibility = Visibility.Visible;
                else
                    PrevPage.Visibility = Visibility.Collapsed;
            }
            catch
            {
                Common.ShowTeachingTip("出现错误", json["msg"].ToString());
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

        private void NavigationView_OnSelectionChanged(NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            page = 0;
            if ((args.SelectedItem as NavigationViewItem).Tag.ToString() == "1")
            {
                SongsSearchResultContainer.Visibility = Visibility.Visible;
                SearchResultContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                SongsSearchResultContainer.Visibility = Visibility.Collapsed;
                SearchResultContainer.Visibility = Visibility.Visible;
            }

            LoadResult();
        }

        private async void SearchKeywordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace((sender as AutoSuggestBox)?.Text))
                try
                {
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchHot);

                    ((AutoSuggestBox)sender).ItemsSource =
                        json["result"]["hots"].ToArray().ToList().Select(t => t["first"].ToString());
                }
                catch (Exception ex)
                {
                    Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
                }
        }

        private void SearchKeywordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ((AutoSuggestBox)sender).ItemsSource = null;
        }

        private void SearchKeywordBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            Text = sender.Text;
            LoadResult();
        }

        private void SearchKeywordBox_SuggestionChosen(AutoSuggestBox sender,
            AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            sender.Text = args.SelectedItem.ToString();
        }

        private async void SearchKeywordBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(sender.Text))
            {
                SearchKeywordBox_GotFocus(sender, null);
                return;
            }

            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchSuggest,
                    new Dictionary<string, object> { { "keywords", sender.Text }, { "type", "mobile" } });

                if (json["result"] != null && json["result"]["allMatch"] != null &&
                    json["result"]["allMatch"].HasValues)
                    sender.ItemsSource = json["result"]["allMatch"].ToArray().ToList()
                        .Select(t => t["keyword"].ToString())
                        .ToList();
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
    }
}