#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Features.History.Services;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.UI.Lists;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Search;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Search : Page
{
    public static readonly DependencyProperty CurrentResultContainerProperty = DependencyProperty.Register(
        nameof(CurrentResultContainer), typeof(ContainerBase), typeof(Search),
        new PropertyMetadata(default(ContainerBase)));

    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();
    private readonly Dictionary<ContainerBase, List<ProvidableItemBase>> _linerSearchItems = new();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly Dictionary<string, ContainerBase> _searchContainers = new(StringComparer.Ordinal);
    private readonly ISearchableProvider _searchProvider = Ioc.Default.GetRequiredService<ISearchableProvider>();

    private readonly IProviderSearchCategoryTypeIds _searchTypeIds =
        Ioc.Default.GetRequiredService<IProviderSearchCategoryTypeIds>();

    private readonly ISearchSuggestionProvidable _suggestionProvider =
        Ioc.Default.GetService<ISearchSuggestionProvidable>();

    private string _cachedSearchText = string.Empty;
    private string _lastSuggestionKeyword = string.Empty;
    private List<string> _lastSuggestions = [];
    private Task _loadResultTask;

    private string _searchText = "";

    public Search()
    {
        InitializeComponent();
        NavigationViewSelector.SelectedItem = NavigationViewSelector.MenuItems[0];
        _cancellationToken = _cancellationTokenSource.Token;
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
            _searchText = (string)e.Parameter;
            _loadResultTask = LoadResult();
        }

        //if (searchText != string.Empty) _ = LoadResult();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Bindings.StopTracking();
        if (_loadResultTask != null && !_loadResultTask.IsCompleted)
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadResultTask;
            }
            catch
            {
                //Ignore
            }

        _cancellationTokenSource?.Dispose();
    }

    private async Task LoadResult()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(_searchText)) return;
        ResetSearchCachesIfKeywordChanged();
        if (Convert.ToBase64String(_searchText.ToByteArrayUtf8()) == "6Ieq5p2A")
        {
            _ = Launcher.LaunchUriAsync(new Uri(@"http://music.163.com/m/topic/18926801"));
            return;
        }

        TBNoRes.Visibility = Visibility.Collapsed;
        _history.AddSearchHistory(_searchText);

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

        await Task.CompletedTask;
        CurrentResultContainer = new DelegateProgressiveContainer(
            (offset, count, cancellationToken) =>
                LoadSearchItemsAsync(typeId, offset, count, cancellationToken),
            _searchText,
            $"search:{typeId}:{_searchText}",
            typeId,
            pageSize: 30);
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

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> LoadSearchItemsAsync(
        string typeId,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = $"{_searchText}\u001f{typeId}";
        if (!_searchContainers.TryGetValue(cacheKey, out var container))
        {
            container = await _searchProvider.SearchProvidableItemsAsync(_searchText, typeId, cancellationToken);
            _searchContainers[cacheKey] = container;
        }

        var result = await GetPagedItemsAsync(container, offset, count, cancellationToken);
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            if (offset == 0)
                TBNoRes.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
        return result;
    }

    private void ResetSearchCachesIfKeywordChanged()
    {
        if (string.Equals(_cachedSearchText, _searchText, StringComparison.Ordinal))
            return;

        _searchContainers.Clear();
        _linerSearchItems.Clear();
        _cachedSearchText = _searchText;
    }

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> GetPagedItemsAsync(
        ContainerBase container,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (container is IProgressiveLoadingContainer progressive)
        {
            var (hasMore, items) = await progressive.GetProgressiveItemsListAsync(
                offset, count, cancellationToken);
            return (hasMore, items ?? []);
        }

        if (container is LinerContainerBase liner)
        {
            if (!_linerSearchItems.TryGetValue(container, out var items))
            {
                items = await liner.GetAllItemsAsync(cancellationToken) ?? [];
                _linerSearchItems[container] = items;
            }

            return (items.Count > offset + count, items.Skip(offset).Take(count).ToList());
        }

        return (false, []);
    }

    private void NavigationView_OnSelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        _loadResultTask = LoadResult();
    }

    private void SearchKeywordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ((AutoSuggestBox)sender).ItemsSource = null;
    }

    private void SearchKeywordBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _searchText = sender.Text;
        _loadResultTask = LoadResult();
    }

    private async void SearchKeywordBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var keyword = sender.Text;
        if (string.IsNullOrEmpty(keyword) || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

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
            _searchText = item.ToString();
            _loadResultTask = LoadResult();
        }
    }
}
