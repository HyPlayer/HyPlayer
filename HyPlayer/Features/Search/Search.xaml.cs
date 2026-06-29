#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Infrastructure.Extensions;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
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
    private readonly ISearchableProvider _searchProvider = Ioc.Default.GetRequiredService<ISearchableProvider>();
    private readonly IProviderSearchCategoryTypeIds _searchTypeIds = Ioc.Default.GetRequiredService<IProviderSearchCategoryTypeIds>();
    private readonly ISearchSuggestionProvidable _suggestionProvider = Ioc.Default.GetService<ISearchSuggestionProvidable>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();

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
    private string _cachedSearchText = string.Empty;

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
        ResetSearchCachesIfKeywordChanged();
        if (Convert.ToBase64String(searchText.ToByteArrayUtf8()) == "6Ieq5p2A")
        {
            _ = Launcher.LaunchUriAsync(new Uri(@"http://music.163.com/m/topic/18926801"));
            return;
        }

        TBNoRes.Visibility = Visibility.Collapsed;
        _history.AddSearchHistory(searchText);

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
        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.SingleSongSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var song in items.OfType<SingleSongBase>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SongResults.Add(await SongListItemViewModel.FromProviderSongAsync(song, page * 30 + i));
            var aliases = song is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
            var translation = song is IHasTranslation translationProvider ? translationProvider.Translation : null;
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = song.Name,
                    LineTwo = string.Join(" / ", song.CreatorList ?? []),
                    LineThree = song.Album?.Name,
                    LineOne = (translation ?? string.Empty) + " / " + string.Join("", aliases ?? []),
                    Route = new AppRoute.Song($"{song.ActualId}"),
                    PlayResource = new MusicResource.Song($"{song.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(song),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadAlbumResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.AlbumSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var album in items.OfType<AlbumBase>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var aliases = album is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
            var description = album is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
            var creators = album is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync(_cancellationToken) : null;
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = album.Name,
                    LineOne = string.Join(" / ", creators?.Select(t => t.Name) ?? []),
                    LineTwo = string.Join(" / ", aliases ?? []),
                    LineThree = description,
                    Route = new AppRoute.Album($"{album.ActualId}"),
                    PlayResource = new MusicResource.Album($"{album.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(album),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadArtistResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.ArtistSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var artist in items.OfType<ArtistBase>())
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
        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.PlaylistSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var playlist in items.OfType<LinerContainerBase>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var description = playlist is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
            var creators = playlist is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync(_cancellationToken) : null;
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = playlist.Name,
                    LineOne = creators?.FirstOrDefault()?.Name,
                    LineTwo = description,
                    LineThree = string.Empty,
                    Route = new AppRoute.Playlist($"{playlist.ActualId}"),
                    PlayResource = new MusicResource.Playlist($"{playlist.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(playlist),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadUserResult()
    {
        var i = 0;
        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.UserSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var user in items.OfType<PersonBase>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var description = user is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = user.Name,
                    LineOne = description,
                    Route = new AppRoute.Me($"{user.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(user),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadRadioResult()
    {
        var i = 0;
        if (_searchTypeIds.RadioChannelSearchTypeId is null)
            return;

        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.RadioChannelSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var radio in items.OfType<LinerContainerBase>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var description = radio is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = radio.Name,
                    LineOne = radio is IHasCreators creatorsProvider
                        ? (await creatorsProvider.GetCreatorsAsync(_cancellationToken))?.FirstOrDefault()?.Name
                        : null,
                    LineTwo = description,
                    LineThree = string.Empty,
                    Route = new AppRoute.Radio($"{radio.ActualId}"),
                    PlayResource = new MusicResource.Radio($"{radio.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(radio),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }



    private async Task LoadMVResult()
    {
        var i = 0;
        if (_searchTypeIds.RichMediaSearchTypeId is null)
            return;

        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.RichMediaSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in items.OfType<RichMediaBase>())
        {
            SearchResultContainer.ListItems.Add(
                new SimpleListItem
                {
                    Title = item.Name,
                    Route = new AppRoute.MV($"{item.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(item),
                    Order = page * 30 + i++
                });
        }

        UpdatePageState(hasMore);
    }

    private async Task LoadMlogResult()
    {
        var i = 0;
        if (_searchTypeIds.ShortVideoSearchTypeId is null)
            return;

        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.ShortVideoSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in items.OfType<RichMediaBase>())
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
        if (_searchTypeIds.LyricSearchTypeId is null)
            return;

        var (hasMore, items) = await LoadSearchItemsAsync(_searchTypeIds.LyricSearchTypeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in items)
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
            container = await _searchProvider.SearchProvidableItemsAsync(searchText, typeId, _cancellationToken);
            _searchContainers[cacheKey] = container;
        }

        return await GetPagedItemsAsync(container);
    }

    private void ResetSearchCachesIfKeywordChanged()
    {
        if (string.Equals(_cachedSearchText, searchText, StringComparison.Ordinal))
            return;

        _searchContainers.Clear();
        _linerSearchItems.Clear();
        _cachedSearchText = searchText;
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

    private static async Task<string?> TryGetCoverLinkAsync(ProvidableItemBase item)
    {
        if (item is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync();
        return result is IResourceResultOf<Uri?> uriResult
            ? (await uriResult.GetResourceAsync())?.GetLeftPart(UriPartial.Path)
            : null;
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
