using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Shell.Search;

public sealed class ShellSearchViewModel
{
    private readonly ISearchSuggestionProvidable _suggestionProvider;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notification;
    private string _lastSuggestionKeyword = string.Empty;
    private IReadOnlyList<string>? _lastSuggestions;

    public ShellSearchViewModel(ISearchSuggestionProvidable suggestionProvider,
                                 INavigationService navigation,
                                 INotificationService notification)
    {
        _suggestionProvider = suggestionProvider;
        _navigation = navigation;
        _notification = notification;
    }

    public async Task<IReadOnlyList<string>?> GetSuggestionsAsync(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return null;
        if (keyword == _lastSuggestionKeyword)
            return _lastSuggestions;

        try
        {
            var container = await _suggestionProvider.GetSearchSuggestionsAsync(keyword);
            var items = container is LinerContainerBase liner ? await liner.GetAllItemsAsync() : [];
            _lastSuggestionKeyword = keyword;
            _lastSuggestions = items.Select(GetSuggestionText).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            return _lastSuggestions;
        }
        catch (System.Exception ex)
        {
            _notification.ShowMessage("获取推荐词失败", ex.Message);
        }

        return null;
    }

    private static string? GetSuggestionText(ProvidableItemBase item)
    {
        return !string.IsNullOrWhiteSpace(item.Name) ? item.Name : item.ActualId;
    }

    public void NavigateToSearch(string keyword)
    {
        _navigation.Navigate(typeof(Features.Search.Search), keyword);
    }
}
