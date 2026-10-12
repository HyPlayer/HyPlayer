using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseApi.Models.ResponseModels;
using HyPlayer.NeteaseProvider.Mappers;
using HyPlayer.Platform.Serialization;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.History.Services;

public sealed class HistoryService : IHistoryService
{
    private const string CurPlayingListHistoryKey = "curPlayingListHistory";
    private const string SongPlayHistoryFileName = "songPlayHistory";
    private const string SongHistoryInfoFileName = "songHistoryInfo";
    private const string UnnamedSongName = "未知歌曲";
    private const int SongHistoryLimit = 100;
    private readonly SemaphoreSlim _songInfoGate = new(1, 1);
    private readonly IProvidableItemRangeProvidable _itemRangeProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;
    private readonly LocalLibrarySettings _setting;

    public HistoryService(
        LocalLibrarySettings setting,
        IProvidableItemRangeProvidable itemRangeProvider,
        IProviderKnownTypeIds knownTypeIds,
        INotificationService notification)
    {
        _setting = setting;
        _itemRangeProvider = itemRangeProvider;
        _knownTypeIds = knownTypeIds;
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

        var actualId = NormalizeStoredSongId(songId, _knownTypeIds.SingleSongTypeId);
        list.RemoveAll(item => string.Equals(
            NormalizeStoredSongId(item, _knownTypeIds.SingleSongTypeId), actualId, StringComparison.Ordinal));
        list.Insert(0, actualId);
        if (list.Count >= SongHistoryLimit)
            list.RemoveRange(SongHistoryLimit, list.Count - SongHistoryLimit);
        ApplicationData.Current.LocalSettings.Values["songHistory"] =
            JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public async Task RememberSongAsync(string songId, SingleSongBase song)
    {
        var actualId = NormalizeStoredSongId(songId, _knownTypeIds.SingleSongTypeId);
        if (string.IsNullOrWhiteSpace(actualId) || !IsNamed(song))
            return;

        var entry = new SongHistoryEntry(
            actualId,
            song.Name,
            song.CreatorList?.Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? [],
            song.Album?.Name,
            await TryGetCoverUrlAsync(song).ConfigureAwait(false),
            song.Duration);
        await _songInfoGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = Remember(await ReadSongInfoAsync().ConfigureAwait(false), entry, SongHistoryLimit);
            var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
                SongHistoryInfoFileName,
                CreationCollisionOption.OpenIfExists);
            await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(entries, JsonDefaults.Options));
        }
        catch
        {
        }
        finally
        {
            _songInfoGate.Release();
        }
    }

    internal static List<SongHistoryEntry> Remember(List<SongHistoryEntry> entries, SongHistoryEntry entry, int limit)
    {
        var result = new List<SongHistoryEntry>(entries.Count + 1) { entry };
        result.AddRange(entries.Where(item => !string.Equals(item.Id, entry.Id, StringComparison.Ordinal)));
        if (result.Count > limit)
            result.RemoveRange(limit, result.Count - limit);
        return result;
    }

    internal static bool IsNamed(SingleSongBase song)
    {
        return !string.IsNullOrWhiteSpace(song.Name) &&
               !string.Equals(song.Name, UnnamedSongName, StringComparison.Ordinal);
    }

    internal static List<SingleSongBase> RestoreUnnamedSongs(
        IReadOnlyList<string> songIds,
        IReadOnlyList<SingleSongBase> resolved,
        IReadOnlyList<SongHistoryEntry> remembered,
        string singleSongTypeId)
    {
        var resolvedById = new Dictionary<string, SingleSongBase>(StringComparer.Ordinal);
        foreach (var song in resolved)
            if (!string.IsNullOrEmpty(song.ActualId))
                resolvedById.TryAdd(song.ActualId, song);

        var rememberedById = new Dictionary<string, SongHistoryEntry>(StringComparer.Ordinal);
        foreach (var entry in remembered)
            rememberedById.TryAdd(entry.Id, entry);

        var result = new List<SingleSongBase>(songIds.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var storedId in songIds)
        {
            var actualId = NormalizeStoredSongId(storedId ?? string.Empty, singleSongTypeId);
            if (string.IsNullOrWhiteSpace(actualId) || !used.Add(actualId))
                continue;

            resolvedById.TryGetValue(actualId, out var song);
            if ((song is null || !IsNamed(song)) && rememberedById.TryGetValue(actualId, out var entry))
                song = CreateSong(entry);
            if (song is not null)
                result.Add(song);
        }

        result.AddRange(resolved.Where(song => string.IsNullOrEmpty(song.ActualId) || !used.Contains(song.ActualId)));
        return result;
    }

    private static SingleSongBase CreateSong(SongHistoryEntry entry)
    {
        return new EmittedSongDto
        {
            Id = entry.Id,
            Name = entry.Name,
            Duration = entry.Duration,
            Album = new AlbumDto { Id = "0", Name = entry.Album, PictureUrl = entry.CoverUrl },
            Artists = entry.Artists.Select(name => new ArtistDto { Id = "0", Name = name }).ToArray()
        }.MapToNeteaseMusic();
    }

    private static async Task<string?> TryGetCoverUrlAsync(SingleSongBase song)
    {
        try
        {
            if (song is not IHasCover coverProvider)
                return null;

            return await coverProvider.GetCoverAsync().ConfigureAwait(false) is IResourceResultOf<Uri?> cover
                ? (await cover.GetResourceAsync().ConfigureAwait(false))?.GetLeftPart(UriPartial.Path)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<SongHistoryEntry>> ReadSongInfoAsync()
    {
        try
        {
            var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
                SongHistoryInfoFileName,
                CreationCollisionOption.OpenIfExists);
            var text = await FileIO.ReadTextAsync(file);
            return string.IsNullOrWhiteSpace(text)
                ? []
                : JsonSerializer.Deserialize<List<SongHistoryEntry>>(text, JsonDefaults.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void AddSearchHistory(string text)
    {
        var list = JsonSerializer.Deserialize<List<string>>(
            ApplicationData.Current.LocalSettings.Values["searchHistory"]?.ToString() ?? "[]",
            JsonDefaults.Options) ?? [];
        list.RemoveAll(item => item == text);
        list.Insert(0, text);
        ApplicationData.Current.LocalSettings.Values["searchHistory"] =
            JsonSerializer.Serialize(list, JsonDefaults.Options);
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
        ApplicationData.Current.LocalSettings.Values["songlistHistory"] =
            JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public async Task SetCurrentPlayingListHistoryAsync(List<string> songIds, int currentIndex)
    {
        var state = new CurPlayingListHistoryState(
            songIds,
            currentIndex >= 0 && currentIndex < songIds.Count ? currentIndex : -1);

        if (_setting.AdvancedMusicHistoryStorage)
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
        else
            ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] =
                JsonSerializer.Serialize(state, JsonDefaults.Options);
    }

    public async Task ClearHistoryAsync()
    {
        var list = new List<string>();
        var values = ApplicationData.Current.LocalSettings.Values;
        values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        values[CurPlayingListHistoryKey] =
            JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
        await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
            SongPlayHistoryFileName,
            CreationCollisionOption.OpenIfExists)).DeleteAsync();
        await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
            SongHistoryInfoFileName,
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
            return RestoreUnnamedSongs(
                songIds,
                await LoadProviderSongsAsync(songIds),
                await ReadSongInfoAsync(),
                _knownTypeIds.SingleSongTypeId);
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
        if (_setting.AdvancedMusicHistoryStorage)
            text = await FileIO.ReadTextAsync(
                await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
                    SongPlayHistoryFileName,
                    CreationCollisionOption.OpenIfExists));
        else
            text = ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey]?.ToString() ?? "[]";

        return ParseCurrentPlayingListHistoryState(text);
    }

    private async Task<List<SingleSongBase>> LoadProviderSongsAsync(List<string> songIds)
    {
        if (songIds.Count == 0)
            return [];

        var providerSongIds = BuildProviderSongIds(songIds, _knownTypeIds.SingleSongTypeId);
        var items = await _itemRangeProvider.GetProvidableItemsRangeAsync(providerSongIds);
        return items.OfType<SingleSongBase>().ToList();
    }

    internal static List<string> BuildProviderSongIds(IEnumerable<string> songIds, string singleSongTypeId)
    {
        return songIds
            .Where(songId => !string.IsNullOrWhiteSpace(songId))
            .Select(songId => singleSongTypeId + NormalizeStoredSongId(songId, singleSongTypeId))
            .ToList();
    }

    private static string NormalizeStoredSongId(string songId, string singleSongTypeId)
    {
        return songId.StartsWith(singleSongTypeId, StringComparison.Ordinal)
            ? songId[singleSongTypeId.Length..]
            : songId;
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
