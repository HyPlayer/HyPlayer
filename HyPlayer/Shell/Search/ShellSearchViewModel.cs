using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Notifications;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Shell.Navigation.Services;

namespace HyPlayer.Shell.Search;

public sealed class ShellSearchViewModel
{
    private readonly INavigationService _navigation;
    private readonly INotificationService _notification;
    private readonly ISearchSuggestionProvidable _suggestionProvider;
    private string _lastSuggestionKeyword = string.Empty;
    private IReadOnlyList<string>? _lastSuggestions;
    private long _suggestionVersion;

    public ShellSearchViewModel(ISearchSuggestionProvidable suggestionProvider,
        INavigationService navigation,
        INotificationService notification)
    {
        _suggestionProvider = suggestionProvider;
        _navigation = navigation;
        _notification = notification;
    }

    public async Task<IReadOnlyList<string>?> GetSuggestionsAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(keyword)) return null;
        if (keyword == _lastSuggestionKeyword)
            return _lastSuggestions;

        var version = Interlocked.Increment(ref _suggestionVersion);

        try
        {
            var container = await _suggestionProvider.GetSearchSuggestionsAsync(keyword, cancellationToken);
            var items = container is LinerContainerBase liner
                ? await liner.GetAllItemsAsync(cancellationToken)
                : [];
            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _suggestionVersion)) return null;
            _lastSuggestionKeyword = keyword;
            _lastSuggestions = items.Select(GetSuggestionText).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            return _lastSuggestions;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
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
