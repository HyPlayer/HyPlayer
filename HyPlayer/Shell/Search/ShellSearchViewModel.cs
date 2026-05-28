using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Shell.Search;

public sealed class ShellSearchViewModel
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly INavigationService _navigation;
    private readonly ITeachingTipService _teachingTipService;

    public ShellSearchViewModel(NeteaseCloudMusicApiHandler api,
                                INavigationService navigation,
                                ITeachingTipService teachingTipService)
    {
        _api = api;
        _navigation = navigation;
        _teachingTipService = teachingTipService;
    }

    public async Task<IReadOnlyList<string>?> GetSuggestionsAsync(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return null;

        var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,
                                           new SearchSuggestionRequest { Keyword = keyword });
        if (json.IsError)
        {
            _teachingTipService.Items.Enqueue(new("获取推荐词失败", json.Error.Message));
            return null;
        }

        return json.Value.Result.AllMatch?.Select(t => t.Keyword).ToList();
    }

    public void NavigateToSearch(string keyword)
    {
        _navigation.Navigate(typeof(Features.Search.Search), keyword);
    }
}