#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using Microsoft.AppCenter.Crashes;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Search : Page, IDisposable
{
    public static readonly DependencyProperty HasNextPageProperty = DependencyProperty.Register(
        "HasNextPage", typeof(bool), typeof(Search), new PropertyMetadata(default(bool)));

    public static readonly DependencyProperty HasPreviousPageProperty = DependencyProperty.Register(
        "HasPreviousPage", typeof(bool), typeof(Search), new PropertyMetadata(default(bool)));

    private readonly ObservableCollection<NCSong> SongResults;
    private int page;
    private string searchText = "";

    public Search()
    {
        InitializeComponent();
        NavigationViewSelector.SelectedItem = NavigationViewSelector.MenuItems[0];
        SongResults = new ObservableCollection<NCSong>();
        NavigationCacheMode = NavigationCacheMode.Required;
        SongResults = new ObservableCollection<NCSong>();
    }

    public bool HasNextPage
    {
        get => (bool)GetValue(HasNextPageProperty);
        set => SetValue(HasNextPageProperty, value);
    }

    public bool HasPreviousPage
    {
        get => (bool)GetValue(HasPreviousPageProperty);
        set => SetValue(HasPreviousPageProperty, value);
    }

    public void Dispose()
    {
        SearchResultContainer.ListItems.Clear();
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e)
    {
        if((string)e.Parameter != null)
        {
            SearchKeywordBox.Text = (string)e.Parameter;
            SearchKeywordBox_QuerySubmitted(SearchKeywordBox, null);
        }

        if (searchText != string.Empty) _ = LoadResult();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        SearchResultContainer.ListItems.Clear();
        GC.Collect();
    }

    private async Task LoadResult()
    {
        if (string.IsNullOrEmpty(searchText)) return;
        if (Convert.ToBase64String(searchText.ToByteArrayUtf8()) == "6Ieq5p2A")
        {
            _ = Launcher.LaunchUriAsync(new Uri(@"http://music.163.com/m/topic/18926801"));
            return;
        }

        TBNoRes.Visibility = Visibility.Collapsed;
        HistoryManagement.AddSearchHistory(searchText);

        SearchResultContainer.ListItems.Clear();
        SongResults.Clear();
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Cloudsearch,
                new Dictionary<string, object>
                {
                    { "keywords", searchText },
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
                case "1002":
                    LoadUserResult(json);
                    break;
                case "1004":
                    LoadMVResult(json);
                    break;
                case "1006":
                    LoadLyricResult(json);
                    break;
                case "1009":
                    LoadRadioResult(json);
                    break;
                case "1014":
                    LoadMlogResult(json);
                    break;
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void LoadMVResult(JObject json)
    {
        var i = 0;
        if (json["result"]["mvCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in json["result"]["mvs"])
        {
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item["name"].ToString(),
                    LineOne = item["artistName"].ToString(),
                    LineTwo = item["briefDesc"]?.ToString(),
                    LineThree = item["transNames"]?.ToString(),
                    ResourceId = "ml" + item["id"],
                    CoverUri = item["cover"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                    Order = i++
                });
            if (json["result"]["mvCount"].ToObject<int>() >= (page + 1) * 30)
                HasNextPage = true;
            else
                HasNextPage = false;
            if (page > 0)
                HasPreviousPage = true;
            else
                HasPreviousPage = false;
        }
    }

    private void LoadMlogResult(JObject json)
    {
        var i = 0;
        if (json["result"]["videoCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in json["result"]["videos"])
        {
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item["title"]?.ToString(),
                    LineOne = item["aliaName"]?.ToString(),
                    LineTwo = item["transName"]?.ToString(),
                    LineThree = item["creator"]?.First?["userName"]?.ToString(),
                    ResourceId = "ml" + item["vid"],
                    CoverUri = item["coverUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                    Order = i++
                });
            if (json["result"]["videoCount"].ToObject<int>() >= (page + 1) * 30)
                HasNextPage = true;
            else
                HasNextPage = false;
            if (page > 0)
                HasPreviousPage = true;
            else
                HasPreviousPage = false;
        }
    }

    private void LoadLyricResult(JObject json)
    {
        var i = 0;
        if (json["result"]["songCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
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
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void LoadUserResult(JObject json)
    {
        var i = 0;
        if (!json["result"].HasValues)
        {
            TBNoRes.Visibility = Visibility.Visible;
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
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void LoadRadioResult(JObject json)
    {
        var i = 0;
        if (json["result"]["djRadiosCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
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
                    Order = i++,
                    CanPlay = true
                });
        if (int.Parse(json["result"]["djRadiosCount"].ToString()) >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void Btn_Click(object sender, RoutedEventArgs e)
    {
        searchText = (sender as Button).Content.ToString();
        _ = LoadResult();
    }

    private void LoadPlaylistResult(JObject json)
    {
        var i = 0;
        if (json["result"]["playlistCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
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
                Order = i++,
                CanPlay = true
            });
        if (int.Parse(json["result"]["playlistCount"].ToString()) >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void LoadArtistResult(JObject json)
    {
        var i = 0;
        if (json["result"]["artistCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var singerjson in json["result"]["artists"].ToArray())
            SearchResultContainer.ListItems.Add(new SimpleListItem
            {
                Title = singerjson["name"].ToString(),
                LineOne = singerjson["trans"].ToString(),
                LineTwo = string.Join(" / ",
                    (singerjson["alia"]?.ToList() ?? new List<JToken>()).Select(t => t.ToString())),
                LineThree = $"专辑数 {singerjson["albumSize"]} | MV 数 {singerjson["mvSize"]}",
                ResourceId = "ar" + singerjson["id"],
                CoverUri = singerjson["img1v1Url"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                Order = i++
            });
        if (int.Parse(json["result"]["artistCount"].ToString()) >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void LoadAlbumResult(JObject json)
    {
        var i = 0;
        if (json["result"]["albumCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
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
                Order = i++,
                CanPlay = true
            });
        if (int.Parse(json["result"]["albumCount"].ToString()) >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void LoadSongResult(JObject json)
    {
        if (json["result"]["songCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
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

            if (int.Parse(json["result"]["songCount"].ToString()) >= (page + 1) * 30)
                HasNextPage = true;
            else
                HasNextPage = false;
            if (page > 0)
                HasPreviousPage = true;
            else
                HasPreviousPage = false;
        }
        catch
        {
            Common.AddToTeachingTipLists("出现错误", json["msg"].ToString());
        }
    }


    private void PrevPage_OnClick(object sender, RoutedEventArgs e)
    {
        page--;
        _ = LoadResult();
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        _ = LoadResult();
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

        _ = LoadResult();
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
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
    }

    private void SearchKeywordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ((AutoSuggestBox)sender).ItemsSource = null;
    }

    private void SearchKeywordBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        searchText = sender.Text;
        _ = LoadResult();
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
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private async void HistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if((sender as ComboBox) is not null)
        {
            SearchKeywordBox.Text= (sender as ComboBox).SelectedItem as String;//将历史放上去
            await LoadResult();
        }
    }

    private void Expander_Expanding(Microsoft.UI.Xaml.Controls.Expander sender, Microsoft.UI.Xaml.Controls.ExpanderExpandingEventArgs args)
    {
        HistoryComboBox.IsDropDownOpen = true;//一展开就展示历史
    }
}