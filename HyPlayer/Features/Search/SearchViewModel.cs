using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Features.History.Services;
using HyPlayer.UI.Lists;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;

namespace HyPlayer.Features.Search;

public partial class SearchViewModel(
    ISearchableProvider searchProvider,
    IProviderSearchCategoryTypeIds searchTypeIds,
    IHistoryService history) : ObservableObject
{
    private readonly Dictionary<ContainerBase, List<ProvidableItemBase>> _linerSearchItems = [];
    private readonly Dictionary<string, ContainerBase> _searchContainers = new(StringComparer.Ordinal);
    private string _cachedKeyword = string.Empty;

    [ObservableProperty] public partial ContainerBase? CurrentResultContainer { get; set; }
    [ObservableProperty] public partial bool HasNoResults { get; set; }
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedCategoryTag { get; set; } = "1";

    public IReadOnlyList<string> SearchHistory => history.GetSearchHistory();

    public void Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return;

        Keyword = keyword;
        HasNoResults = false;
        ResetCachesIfKeywordChanged();
        history.AddSearchHistory(keyword);
        CurrentResultContainer = null;

        var typeId = GetCurrentSearchTypeId();
        if (string.IsNullOrWhiteSpace(typeId))
            return;

        CurrentResultContainer = new DelegateProgressiveContainer(
            (offset, count, cancellationToken) =>
                LoadSearchItemsAsync(typeId, offset, count, cancellationToken),
            keyword,
            $"search:{typeId}:{keyword}",
            typeId,
            pageSize: 30);
    }

    public void SelectCategory(string categoryTag)
    {
        if (string.IsNullOrWhiteSpace(categoryTag) ||
            string.Equals(SelectedCategoryTag, categoryTag, StringComparison.Ordinal))
            return;

        SelectedCategoryTag = categoryTag;
        Search(Keyword);
    }

    private string? GetCurrentSearchTypeId()
    {
        return SelectedCategoryTag switch
        {
            "1" => searchTypeIds.SingleSongSearchTypeId,
            "10" => searchTypeIds.AlbumSearchTypeId,
            "100" => searchTypeIds.ArtistSearchTypeId,
            "1000" => searchTypeIds.PlaylistSearchTypeId,
            "1002" => searchTypeIds.UserSearchTypeId,
            "1004" => searchTypeIds.RichMediaSearchTypeId,
            "1006" => searchTypeIds.LyricSearchTypeId,
            "1009" => searchTypeIds.RadioChannelSearchTypeId,
            "1014" => searchTypeIds.ShortVideoSearchTypeId,
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
        var cacheKey = $"{Keyword}\u001f{typeId}";
        if (!_searchContainers.TryGetValue(cacheKey, out var container))
        {
            container = await searchProvider.SearchProvidableItemsAsync(Keyword, typeId, cancellationToken);
            _searchContainers[cacheKey] = container;
        }

        var result = await GetPagedItemsAsync(container, offset, count, cancellationToken);
        if (offset == 0)
            HasNoResults = result.Items.Count == 0;
        return result;
    }

    private void ResetCachesIfKeywordChanged()
    {
        if (string.Equals(_cachedKeyword, Keyword, StringComparison.Ordinal))
            return;

        _searchContainers.Clear();
        _linerSearchItems.Clear();
        _cachedKeyword = Keyword;
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
}
