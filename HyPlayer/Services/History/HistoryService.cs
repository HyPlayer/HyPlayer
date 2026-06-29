using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Serialization;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Services.History;

public sealed class HistoryService : IHistoryService
{
    private const string CurPlayingListHistoryKey = "curPlayingListHistory";
    private const string SongPlayHistoryFileName = "songPlayHistory";
    private readonly Setting _setting;
    private readonly IProvidableItemRangeProvidable _itemRangeProvider;
    private readonly INotificationService _notification;

    public HistoryService(
        Setting setting,
        IProvidableItemRangeProvidable itemRangeProvider,
        INotificationService notification)
    {
        _setting = setting;
        _itemRangeProvider = itemRangeProvider;
        _notification = notification;
    }

    public void InitializeHistoryTrack()
    {
        var list = new List<string>();
        var values = ApplicationData.Current.LocalSettings.Values;
        if (values["songHistory"] == null)
            values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        if (values["songHistory"].ToString().StartsWith("[{"))
            values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        if (values["searchHistory"] == null)
            values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        if (values["songlistHistory"] == null)
            values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        if (values[CurPlayingListHistoryKey] == null)
            values[CurPlayingListHistoryKey] =
                JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
        if (values[CurPlayingListHistoryKey].ToString().StartsWith("[{"))
            values[CurPlayingListHistoryKey] =
                JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
        if (values["songlistHistory"].ToString().StartsWith("[{"))
            values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public void AddNCSongHistory(string songId)
    {
        var list = JsonSerializer.Deserialize<List<string>>(
            ApplicationData.Current.LocalSettings.Values["songHistory"]?.ToString() ?? "[]",
            JsonDefaults.Options) ?? [];

        list.Remove(songId);
        list.Insert(0, songId);
        if (list.Count >= 100)
            list.RemoveRange(100, list.Count - 100);
        ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public void AddSearchHistory(string text)
    {
        var list = JsonSerializer.Deserialize<List<string>>(
            ApplicationData.Current.LocalSettings.Values["searchHistory"]?.ToString() ?? "[]",
            JsonDefaults.Options) ?? [];
        list.RemoveAll(item => item == text);
        list.Insert(0, text);
        ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public void AddSonglistHistory(string playlistId)
    {
        var list = JsonSerializer.Deserialize<List<string>>(
            ApplicationData.Current.LocalSettings.Values["songlistHistory"]?.ToString() ?? "[]",
            JsonDefaults.Options) ?? [];

        list.Remove(playlistId);
        list.Insert(0, playlistId);
        if (list.Count >= 100)
            list.RemoveRange(100, list.Count - 100);
        ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public async Task SetCurrentPlayingListHistoryAsync(List<string> songIds, int currentIndex)
    {
        var state = new CurPlayingListHistoryState(
            songIds,
            currentIndex >= 0 && currentIndex < songIds.Count ? currentIndex : -1);

        if (_setting.advancedMusicHistoryStorage)
        {
            try
            {
                var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
                    SongPlayHistoryFileName,
                    CreationCollisionOption.OpenIfExists);
                await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(state, JsonDefaults.Options));
            }
            catch
            {
            }
        }
        else
        {
            ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] =
                JsonSerializer.Serialize(state, JsonDefaults.Options);
        }
    }

    public async Task ClearHistoryAsync()
    {
        var list = new List<string>();
        var values = ApplicationData.Current.LocalSettings.Values;
        values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        values[CurPlayingListHistoryKey] = JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
        await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
            SongPlayHistoryFileName,
            CreationCollisionOption.OpenIfExists)).DeleteAsync();
    }

    public async Task ClearCurrentPlayingListHistoryAsync()
    {
        await SetCurrentPlayingListHistoryAsync([], -1);
    }

    public async Task<List<SingleSongBase>> GetSongHistoryAsync()
    {
        try
        {
            var songIds = JsonSerializer.Deserialize<List<string>>(
                ApplicationData.Current.LocalSettings.Values["songHistory"]?.ToString() ?? "[]",
                JsonDefaults.Options) ?? [];
            return await LoadProviderSongsAsync(songIds);
        }
        catch (Exception e)
        {
            _notification.ShowMessage("储存歌曲记录时发生错误", e.Message);
        }

        return [];
    }

    public List<string> GetSearchHistory()
    {
        return JsonSerializer.Deserialize<List<string>>(
            ApplicationData.Current.LocalSettings.Values["searchHistory"]?.ToString() ?? "[]",
            JsonDefaults.Options) ?? [];
    }

    public async Task<List<SingleSongBase>> GetCurrentPlayingListHistoryAsync()
    {
        return (await GetCurrentPlayingListHistoryStateAsync()).Songs;
    }

    public async Task<CurPlayingListHistoryResult> GetCurrentPlayingListHistoryStateAsync()
    {
        var resultSongs = new List<SingleSongBase>();
        var historyState = await ReadCurrentPlayingListHistoryStateAsync();
        var trackIds = historyState.SongIds;

        if (trackIds == null || trackIds.Count == 0)
            return new CurPlayingListHistoryResult(resultSongs, -1);
        var nowIndex = 0;
        while (nowIndex * 500 < trackIds.Count)
        {
            var nowIds = trackIds.GetRange(nowIndex * 500, Math.Min(500, trackIds.Count - nowIndex * 500));
            var songs = await LoadProviderSongsAsync(nowIds);
            nowIndex++;
            resultSongs.AddRange(songs);
        }

        var currentIndex = historyState.CurrentIndex;
        if (currentIndex < 0 || currentIndex >= resultSongs.Count)
            currentIndex = resultSongs.Count > 0 ? 0 : -1;

        return new CurPlayingListHistoryResult(resultSongs, currentIndex);
    }

    private async Task<CurPlayingListHistoryState> ReadCurrentPlayingListHistoryStateAsync()
    {
        string text;
        if (_setting.advancedMusicHistoryStorage)
        {
            text = await FileIO.ReadTextAsync(
                await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
                    SongPlayHistoryFileName,
                    CreationCollisionOption.OpenIfExists));
        }
        else
        {
            text = ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey]?.ToString() ?? "[]";
        }

        return ParseCurrentPlayingListHistoryState(text);
    }

    private async Task<List<SingleSongBase>> LoadProviderSongsAsync(List<string> songIds)
    {
        if (songIds.Count == 0)
            return [];

        var items = await _itemRangeProvider.GetProvidableItemsRangeAsync(songIds);
        return items.OfType<SingleSongBase>().ToList();
    }

    private static CurPlayingListHistoryState ParseCurrentPlayingListHistoryState(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CurPlayingListHistoryState([], -1);

        try
        {
            if (text.TrimStart().StartsWith("["))
            {
                var oldList = JsonSerializer.Deserialize<List<string>>(text, JsonDefaults.Options) ?? [];
                return new CurPlayingListHistoryState(oldList, 0);
            }

            var state = JsonSerializer.Deserialize<CurPlayingListHistoryState>(text, JsonDefaults.Options);
            return state ?? new CurPlayingListHistoryState([], -1);
        }
        catch
        {
            var oldList = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
            return new CurPlayingListHistoryState(oldList, 0);
        }
    }
}
