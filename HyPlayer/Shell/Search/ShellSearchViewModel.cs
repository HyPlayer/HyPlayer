using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Pages;

public sealed class ShellSearchViewModel
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notification;

    public ShellSearchViewModel(NeteaseCloudMusicApiHandler api,
                                INavigationService navigation,
                                INotificationService notification)
    {
        _api = api;
        _navigation = navigation;
        _notification = notification;
    }

    public async Task<IReadOnlyList<string>?> GetSuggestionsAsync(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return null;

        var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,
                                           new SearchSuggestionRequest { Keyword = keyword });
        if (json.IsError)
        {
            _notification.ShowMessage("获取推荐词失败", json.Error.Message);
            return null;
        }

        return json.Value.Result.AllMatch?.Select(t => t.Keyword).ToList();
    }

    public void NavigateToSearch(string keyword)
    {
        _navigation.Navigate(typeof(Search), keyword);
    }
}