#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
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

    public static readonly DependencyProperty CurrentResultContainerProperty = DependencyProperty.Register(
        nameof(CurrentResultContainer), typeof(ContainerBase), typeof(Search), new PropertyMetadata(default(ContainerBase)));

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

    public ContainerBase CurrentResultContainer
    {
        get => (ContainerBase)GetValue(CurrentResultContainerProperty);
        set => SetValue(CurrentResultContainerProperty, value);
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

        CurrentResultContainer = null;
        try
        {
            await LoadCurrentCategoryResult();
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadCurrentCategoryResult()
    {
        var typeId = GetCurrentSearchTypeId();
        if (string.IsNullOrWhiteSpace(typeId))
            return;

        var (hasMore, items) = await LoadSearchItemsAsync(typeId);
        if (items.Count is 0)
        {
            TBNoRes.Visibility = Visibility.Visible;
            return;
        }

        CurrentResultContainer = new StaticItemsContainer(items, searchText, $"{typeId}:{page}", typeId);
        UpdatePageState(hasMore);
        Bindings.Update();
    }

    private string? GetCurrentSearchTypeId()
    {
        return ((NavigationViewItem)NavigationViewSelector.SelectedItem).Tag.ToString() switch
        {
            "1" => _searchTypeIds.SingleSongSearchTypeId,
            "10" => _searchTypeIds.AlbumSearchTypeId,
            "100" => _searchTypeIds.ArtistSearchTypeId,
            "1000" => _searchTypeIds.PlaylistSearchTypeId,
            "1002" => _searchTypeIds.UserSearchTypeId,
            "1004" => _searchTypeIds.RichMediaSearchTypeId,
            "1006" => _searchTypeIds.LyricSearchTypeId,
            "1009" => _searchTypeIds.RadioChannelSearchTypeId,
            "1014" => _searchTypeIds.ShortVideoSearchTypeId,
            _ => null
        };
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