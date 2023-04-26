#region

using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

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

    private readonly ObservableCollection<NCSong> SongResults = new ObservableCollection<NCSong>();
    private int page;
    private string searchText = "";
    public bool IsDisposed = false;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _loadResultTask;

    public Search()
    {
        InitializeComponent();
        NavigationViewSelector.SelectedItem = NavigationViewSelector.MenuItems[0];
        _cancellationToken = _cancellationTokenSource.Token;
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
        if (IsDisposed) return;
        SongResults.Clear();
        SearchResultContainer.ListItems.Clear();
        _cancellationTokenSource.Dispose();
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if ((string)e.Parameter != null)
        {
            SearchKeywordBox.Text = (string)e.Parameter;
            SearchKeywordBox_QuerySubmitted(SearchKeywordBox, null);
        }

        //if (searchText != string.Empty) _ = LoadResult();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_loadResultTask != null && !_loadResultTask .IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadResultTask ;
            }
            catch
            {
                Dispose();
                return;
            }
        }
        Dispose();
    }

    private async Task LoadResult()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
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
            if (ex.GetType() != typeof(TaskCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void LoadMVResult(JObject json)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
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
                    CoverUri = item["cover"].ToString(),
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (json["result"]["videoCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in json["result"]["videos"])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item["title"]?.ToString(),
                    LineOne = item["aliaName"]?.ToString(),
                    LineTwo = item["transName"]?.ToString(),
                    LineThree = item["creator"]?.First?["userName"]?.ToString(),
                    ResourceId = "ml" + item["vid"],
                    CoverUri = item["coverUrl"].ToString(),
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (json["result"]["songCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var songJs in json["result"]["songs"].ToArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = songJs["name"].ToString(),
                    LineOne = string.Join(" / ", songJs["ar"].Select(t => t["name"].ToString())),
                    LineTwo = songJs["lyrics"].ToList().First(t => t.ToString().Contains("</b>")).ToString(),
                    LineThree = string.Join("\r\n", songJs["lyrics"].ToList()),
                    ResourceId = "ns" + songJs["id"],
                    CoverUri = songJs["al"]["picUrl"].ToString(),
                    Order = i++
                });
        }
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (!json["result"].HasValues)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var userJs in json["result"]["userprofiles"].ToArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = userJs["nickname"].ToString(),
                    LineOne = userJs["signature"].ToString(),
                    LineTwo = "",
                    LineThree = "",
                    ResourceId = "us" + userJs["userId"],
                    CoverUri = userJs["avatarUrl"].ToString(),
                    Order = i++
                });
        }
            
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (json["result"]["djRadiosCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var pljs in json["result"]["djRadios"].ToArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = pljs["name"].ToString(),
                    LineOne = pljs["dj"]["nickname"].ToString(),
                    LineTwo = pljs["desc"].ToString(),
                    LineThree = pljs["rcmdText"].ToString(),
                    ResourceId = "rd" + pljs["id"],
                    CoverUri = pljs["picUrl"].ToString(),
                    Order = i++,
                    CanPlay = true
                });   
        }
            
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        searchText = (sender as Button).Content.ToString();
        _loadResultTask = LoadResult();
    }

    private void LoadPlaylistResult(JObject json)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (json["result"]["playlistCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var pljs in json["result"]["playlists"].ToArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(new SimpleListItem
            {
                Title = pljs["name"].ToString(),
                LineOne = pljs["creator"]["nickname"].ToString(),
                LineTwo = pljs["description"].ToString(),
                LineThree = $"{pljs["trackCount"]}首 | 播放{pljs["playCount"]}次 | 收藏 {pljs["bookCount"]}次",
                ResourceId = "pl" + pljs["id"],
                CoverUri = pljs["coverImgUrl"].ToString(),
                Order = i++,
                CanPlay = true
            });
        }
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (json["result"]["artistCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var singerjson in json["result"]["artists"].ToArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(new SimpleListItem
            {
                Title = singerjson["name"].ToString(),
                LineOne = singerjson["trans"].ToString(),
                LineTwo = string.Join(" / ",
                    (singerjson["alia"]?.ToList() ?? new List<JToken>()).Select(t => t.ToString())),
                LineThree = $"专辑数 {singerjson["albumSize"]} | MV 数 {singerjson["mvSize"]}",
                ResourceId = "ar" + singerjson["id"],
                CoverUri = singerjson["img1v1Url"].ToString(),
                Order = i++
            });
        }
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        var i = 0;
        if (json["result"]["albumCount"].ToObject<int>() == 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var albumjson in json["result"]["albums"].ToArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(new SimpleListItem
            {
                Title = albumjson["name"].ToString(),
                LineOne = albumjson["artist"]["name"].ToString(),
                LineTwo = albumjson["alias"] != null
                    ? string.Join(" / ", albumjson["alias"].ToArray().Select(t => t.ToString()))
                    : "",
                LineThree = albumjson.Value<bool>("paid") ? "付费专辑" : "",
                ResourceId = "al" + albumjson["id"],
                CoverUri = albumjson["picUrl"].ToString(),
                Order = i++,
                CanPlay = true
            });
        }
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
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
                _cancellationToken.ThrowIfCancellationRequested();
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
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void PrevPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        page--;
        _loadResultTask = LoadResult();
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        page++;
        _loadResultTask = LoadResult();
    }

    private void NavigationView_OnSelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
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

        _loadResultTask = LoadResult();
    }

    private void SearchKeywordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        ((AutoSuggestBox)sender).ItemsSource = null;
    }

    private void SearchKeywordBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        searchText = sender.Text;
        _loadResultTask = LoadResult();
    }

    private async void SearchKeywordBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        if (string.IsNullOrEmpty(sender.Text) || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Search));
        if ((sender as ComboBox) is not null)
        {
            SearchKeywordBox.Text = (sender as ComboBox).SelectedItem as String;//将历史放上去
            _loadResultTask = LoadResult();
        }
    }

    private void SearchKeywordBox_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = (string)args.SelectedItem;
    }
}