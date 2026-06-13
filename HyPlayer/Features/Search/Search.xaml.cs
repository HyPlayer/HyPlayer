#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Infrastructure.Extensions;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.History;
using HyPlayer.UI.Lists;
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
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Search;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Search : Page
{
    private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider = Ioc.Default.GetRequiredService<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
    private readonly ISearchSuggestionProvidable _suggestionProvider = Ioc.Default.GetService<ISearchSuggestionProvidable>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

    public static readonly DependencyProperty HasNextPageProperty = DependencyProperty.Register(
        "HasNextPage", typeof(bool), typeof(Search), new PropertyMetadata(default(bool)));

    public static readonly DependencyProperty HasPreviousPageProperty = DependencyProperty.Register(
        "HasPreviousPage", typeof(bool), typeof(Search), new PropertyMetadata(default(bool)));

    private readonly ObservableCollection<SongListItemViewModel> SongResults = [];
    private int page;
    private string searchText = "";
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _loadResultTask;
    private readonly Dictionary<string, ContainerBase> _searchContainers = new(StringComparer.Ordinal);
    private readonly Dictionary<ContainerBase, List<ProvidableItemBase>> _linerSearchItems = new();
    private string _lastSuggestionKeyword = string.Empty;
    private List<string> _lastSuggestions = [];

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
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.SingleSong);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var song in items.OfType<NeteaseSong>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SongResults.Add(await SongListItemViewModel.FromProviderSongAsync(song, page * 30 + i));
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = song.Name,
                    LineTwo = string.Join(" / ", song.CreatorList ?? []),
                    LineThree = song.Album?.Name,
                    LineOne = (song.Translation ?? string.Empty) + " / " + string.Join("", song.Alias ?? []),
                    Route = new AppRoute.Song($"{song.ActualId}"),
                    PlayResource = new MusicResource.Song($"{song.ActualId}"),
                    CoverLink = song.CoverUrl,
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadAlbumResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.Album);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var album in items.OfType<NeteaseAlbum>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = album.Name,
                    LineOne = string.Join(" / ", album.CreatorList ?? album.Artists?.Select(t => t.Name) ?? []),
                    LineTwo = string.Join(" / ", album.Alias ?? []),
                    LineThree = album.Description,
                    Route = new AppRoute.Album($"{album.ActualId}"),
                    PlayResource = new MusicResource.Album($"{album.ActualId}"),
                    CoverLink = album.PictureUrl,
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadArtistResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.Artist);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var artist in items.OfType<NeteaseArtist>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(new SimpleListItem
            {
                Title = artist.Name,
                Route = new AppRoute.Artist($"{artist.ActualId}"),
                PlayResource = new MusicResource.Artist($"{artist.ActualId}"),
                Order = page * 30 + i++,
                CanPlay = true
            });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadPlaylistResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.Playlist);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var playlist in items.OfType<NeteasePlaylist>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = playlist.Name,
                    LineOne = playlist.Creator?.Name ?? playlist.CreatorList?.FirstOrDefault(),
                    LineTwo = playlist.Description,
                    LineThree = $"歌曲数:{playlist.TrackCount}",
                    Route = new AppRoute.Playlist($"{playlist.ActualId}"),
                    PlayResource = new MusicResource.Playlist($"{playlist.ActualId}"),
                    CoverLink = playlist.CoverUrl,
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadUserResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.User);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var user in items.OfType<NeteaseUser>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = user.Name,
                    LineOne = user.Description,
                    Route = new AppRoute.Me($"{user.ActualId}"),
                    CoverLink = user.AvatarUrl,
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadRadioResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.RadioChannel);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var radio in items.OfType<NeteaseRadioChannel>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = radio.Name,
                    LineOne = radio.CreatorList?.FirstOrDefault(),
                    LineTwo = radio.Description,
                    LineThree = $"节目数:{radio.ProgramCount}",
                    Route = new AppRoute.Radio($"{radio.ActualId}"),
                    PlayResource = new MusicResource.Radio($"{radio.ActualId}"),
                    CoverLink = radio.CoverUrl,
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }



    private async Task LoadMVResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.Mv);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in items.OfType<NeteaseMv>())
        {
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item.Name,
                    Route = new AppRoute.MV($"{item.ActualId}"),
                    CoverLink = item.CoverUrl,
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadMlogResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.MBlog);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in items.OfType<NeteaseVideo>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item.Name,
                    Route = new AppRoute.MV($"{item.ActualId}"),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadLyricResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(NeteaseTypeIds.Lyric);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in items.OfType<NeteaseLyricSearchItem>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item.Name,
                    Route = new AppRoute.Song($"{item.ActualId}"),
                    PlayResource = new MusicResource.Song($"{item.ActualId}"),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> LoadSearchItemsAsync(string typeId)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = $"{searchText}\u001f{typeId}";
        if (!_searchContainers.TryGetValue(cacheKey, out var container))
        {
            container = await _neteaseProvider.SearchProvidableItemsAsync(searchText, typeId, _cancellationToken);
            _searchContainers[cacheKey] = container;
        }

        return await GetPagedItemsAsync(container);
    }

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> GetPagedItemsAsync(ContainerBase container)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (container is IProgressiveLoadingContainer progressive)
        {
            var (hasMore, items) = await progressive.GetProgressiveItemsListAsync(page * 30, 30, _cancellationToken);
            return (hasMore, items ?? []);
        }

        if (container is LinerContainerBase liner)
        {
            if (!_linerSearchItems.TryGetValue(container, out var items))
            {
                items = await liner.GetAllItemsAsync(_cancellationToken) ?? [];
                _linerSearchItems[container] = items;
            }

            return (items.Count > (page + 1) * 30, items.Skip(page * 30).Take(30).ToList());
        }

        return (false, []);
    }

    private void UpdatePageState(bool hasMore)
    {
        HasNextPage = hasMore;
        HasPreviousPage = page > 0;
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
        page = 0;
        _loadResultTask = LoadResult();
    }

    private async void SearchKeywordBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var keyword = sender.Text;
        if (string.IsNullOrEmpty(keyword) || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        try
        {
            if (_suggestionProvider is null)
            {
                sender.ItemsSource = null;
                return;
            }

            if (keyword != _lastSuggestionKeyword)
            {
                var container = await _suggestionProvider.GetSearchSuggestionsAsync(keyword);
                var items = container is LinerContainerBase liner
                    ? await liner.GetAllItemsAsync(_cancellationToken)
                    : [];
                _lastSuggestionKeyword = keyword;
                _lastSuggestions = items.Select(t => !string.IsNullOrWhiteSpace(t.Name) ? t.Name : t.ActualId)
                                        .Where(t => !string.IsNullOrWhiteSpace(t))
                                        .ToList();
            }

            if (sender.Text == keyword)
                sender.ItemsSource = _lastSuggestions;
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
