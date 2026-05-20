#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Infrastructure.Extensions;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.NeteaseApi.Bases;
using HyPlayer.NeteaseApi.Models;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.History;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Search;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Search : Page
{
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

    public static readonly DependencyProperty HasNextPageProperty = DependencyProperty.Register(
        "HasNextPage", typeof(bool), typeof(Search), new PropertyMetadata(default(bool)));

    public static readonly DependencyProperty HasPreviousPageProperty = DependencyProperty.Register(
        "HasPreviousPage", typeof(bool), typeof(Search), new PropertyMetadata(default(bool)));

    private readonly ObservableCollection<NCSong> SongResults = new ObservableCollection<NCSong>();
    private int page;
    private string searchText = "";
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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if ((string)e.Parameter != null)
        {
            searchText = (string)e.Parameter;
            _loadResultTask = LoadResult();
        }

        //if (searchText != string.Empty) _ = LoadResult();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_loadResultTask != null && !_loadResultTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadResultTask;
            }
            catch
            {
                //Ignore
            }
        }

        _cancellationTokenSource?.Dispose();
    }

    private async Task LoadResult()
    {
        _cancellationToken.ThrowIfCancellationRequested();
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
            switch (((NavigationViewItem)NavigationViewSelector.SelectedItem).Tag.ToString())
            {
                case "1":
                    await LoadSongResult();
                    break;
                case "10":
                    await LoadAlbumResult();
                    break;
                case "100":
                    await LoadArtistResult();
                    break;
                case "1000":
                    await LoadPlaylistResult();
                    break;
                case "1002":
                    await LoadUserResult();
                    break;
                case "1004":
                    await LoadMVResult();
                    break;
                case "1006":
                    await LoadLyricResult();
                    break;
                case "1009":
                    await LoadRadioResult();
                    break;
                case "1014":
                    await LoadMlogResult();
                    break;
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadSongResult()
    {
        var json = await _api.RequestAsync
        <SearchSongResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.Song,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索歌曲时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var songJs in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SongResults.Add(songJs.MapNcSong());
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = songJs.Name,
                    LineTwo = string.Join(" / ", songJs.Artists?.Select(t => t.Name) ?? []),
                    LineThree = songJs.Album?.Name,
                    LineOne = string.Join(" ", songJs.Translations ?? []) + " / " + string.Join("", songJs.Alias ?? []),
                    Route = new AppRoute.Song($"{songJs.Id}"),
                    PlayResource = new MusicResource.Song($"{songJs.Id}"),
                    CoverLink = songJs.Album?.PictureUrl,
                    Order = i++
                });
            if (json.Value.Result?.Count >= (page + 1) * 30)
                HasNextPage = true;
            else
                HasNextPage = false;
            if (page > 0)
                HasPreviousPage = true;
            else
                HasPreviousPage = false;
        }
    }

    private async Task LoadAlbumResult()
    {
        var json = await _api.RequestAsync
        <SearchAlbumResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.Album,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索专辑时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var albumJs in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = albumJs.Name,
                    LineOne = string.Join(" / ", albumJs.Artists?.Select(t => t.Name) ?? []),
                    LineTwo = string.Join(" / ", albumJs.Alias ?? []),
                    LineThree = $"歌曲数:{albumJs.Size}",
                    Route = new AppRoute.Album($"{albumJs.Id}"),
                    PlayResource = new MusicResource.Album($"{albumJs.Id}"),
                    CoverLink = albumJs.PictureUrl,
                    Order = i++
                });
        }

        if (json.Value.Result?.Count >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;

        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private async Task LoadArtistResult()
    {
        var json = await _api.RequestAsync
        <SearchArtistResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.Artist,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索歌手时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var singerjson in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(new SimpleListItem
            {
                Title = singerjson.Name,
                LineOne = singerjson.Translation,
                LineTwo = string.Join("/", singerjson.Alias ?? []),
                LineThree = $"专辑数 {singerjson.AlbumSize} | MV 数 {singerjson.MvSize}",
                Route = new AppRoute.Artist($"{singerjson.Id}"),
                PlayResource = new MusicResource.Artist($"{singerjson.Id}"),
                CoverLink = singerjson.Img1v1Url,
                Order = i++,
                CanPlay = true
            });
        }

        if (json.Value.Result?.Count >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;

        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private async Task LoadPlaylistResult()
    {
        var json = await _api.RequestAsync
        <SearchPlaylistResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.Playlist,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索歌单时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var playlistJs in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = playlistJs.Name,
                    LineOne = playlistJs.Creator?.Nickname,
                    LineTwo = playlistJs.Description,
                    LineThree = $"歌曲数:{playlistJs.TrackCount}",
                    Route = new AppRoute.Playlist($"{playlistJs.Id}"),
                    PlayResource = new MusicResource.Playlist($"{playlistJs.Id}"),
                    CoverLink = playlistJs.CoverUrl,
                    Order = i++
                });
        }

        if (json.Value.Result?.Count >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;

        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private async Task LoadUserResult()
    {
        var json = await _api.RequestAsync
        <SearchUserResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.User,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索用户时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var userJs in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = userJs.Nickname,
                    LineOne = userJs.Signature,
                    Route = new AppRoute.Me($"{userJs.UserId}"),
                    CoverLink = userJs.AvatarUrl,
                    Order = i++
                });
        }

        if (json.Value.Result?.Count >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;

        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private async Task LoadRadioResult()
    {
        var json = await _api.RequestAsync
        <SearchRadioResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.RadioChannel,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索电台时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var radioJs in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = radioJs.Name,
                    LineOne = radioJs.DjData?.Nickname,
                    LineTwo = radioJs.Description,
                    LineThree = $"节目数:{radioJs.ProgramCount}",
                    Route = new AppRoute.Radio($"{radioJs.Id}"),
                    PlayResource = new MusicResource.Radio($"{radioJs.Id}"),
                    CoverLink = radioJs.CoverUrl,
                    Order = i++
                });
        }

        if (json.Value.Result?.Count >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;

        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }



    private async Task LoadMVResult()
    {
        var json = await _api.RequestAsync
        <SearchMVResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.MV,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索 MV 时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in json.Value.Result?.Items ?? [])
        {
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item.Name,
                    LineOne = item.ArtistName,
                    LineTwo = item.Description,
                    LineThree = string.Join(" / ", item.TransNames),
                    Route = new AppRoute.MV($"{item.Id}"),
                    CoverLink = item.Cover.ToString(),
                    Order = i++
                });
            if (json.Value.Result?.Count >= (page + 1) * 30)
                HasNextPage = true;
            else
                HasNextPage = false;
            if (page > 0)
                HasPreviousPage = true;
            else
                HasPreviousPage = false;
        }
    }

    private async Task LoadMlogResult()
    {
        var json = await _api.RequestAsync
        <SearchVideoResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.Video,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        var i = 0;
        if (json.IsError)
        {
            _notification.ShowMessage("搜索 Mlog 时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in json.Value.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item.Title,
                    LineOne = string.Join(" / ", item.Artists?.Select(t => t.UserName) ?? []),
                    LineTwo = null,
                    LineThree = null,
                    Route = new AppRoute.MV($"{item.Id}"),
                    CoverLink = item.CoverUrl,
                    Order = i++
                });
            if (json.Value.Result?.Count >= (page + 1) * 30)
                HasNextPage = true;
            else
                HasNextPage = false;
            if (page > 0)
                HasPreviousPage = true;
            else
                HasPreviousPage = false;
        }
    }

    private async Task LoadLyricResult()
    {
        var i = 0;
        var json = await _api.RequestAsync
        <SearchLyricResponse,
            SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,
            new SearchRequest()
            {
                Keyword = searchText,
                Type = NeteaseResourceType.Lyric,
                Limit = 30,
                Offset = page * 30
            }, _cancellationToken);
        if (json.IsError)
        {
            _notification.ShowMessage("搜索歌词时出错", json.Error.Message);
            return;
        }

        if (json.Value?.Result?.Count is null or 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var songJs in json.Value?.Result?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = songJs.Name,
                    LineOne = string.Join(" / ", songJs.Artists?.Select(t => t.Name) ?? []),
                    LineTwo = songJs.Lyrics?.First(t => t.Contains("</b>")),
                    LineThree = string.Join("   ", songJs.Lyrics?.ToList() ?? []),
                    Route = new AppRoute.Song($"{songJs.Id}"),
                    PlayResource = new MusicResource.Song($"{songJs.Id}"),
                    CoverLink = songJs.Album?.PictureUrl,
                    Order = i++
                });
        }

        if (json.Value?.Result?.Count >= (page + 1) * 30)
            HasNextPage = true;
        else
            HasNextPage = false;
        if (page > 0)
            HasPreviousPage = true;
        else
            HasPreviousPage = false;
    }

    private void PrevPage_OnClick(object sender, RoutedEventArgs e)
    {
        page--;
        _loadResultTask = LoadResult();
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        _loadResultTask = LoadResult();
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

        _loadResultTask = LoadResult();
    }

    private void SearchKeywordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ((AutoSuggestBox)sender).ItemsSource = null;
    }

    private void SearchKeywordBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        searchText = sender.Text;
        _loadResultTask = LoadResult();
    }

    private async void SearchKeywordBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(sender.Text) || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        try
        {
            var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,
                new SearchSuggestionRequest()
                {
                    Keyword = sender.Text
                }, _cancellationToken);

            if (json.IsError)
            {
                _notification.ShowMessage("搜索建议时出错", json.Error.Message);
                return;
            }

            sender.ItemsSource = json.Value.Result?.AllMatch?.Select(t => t.Keyword)?.ToList() ?? [];
        }
        catch (Exception ex)
        {
            _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void HistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = sender?.As<ComboBox>()?.SelectedItem;
        if (item is not null)
        {
            searchText = item.ToString();
            _loadResultTask = LoadResult();
        }
    }
}
