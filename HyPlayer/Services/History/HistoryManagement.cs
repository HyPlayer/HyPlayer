using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.Infrastructure.Serialization;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Services.History
{
    public sealed record CurPlayingListHistoryState(List<string> SongIds, int CurrentIndex);

    public class HistoryManagement
    {
        private const string CurPlayingListHistoryKey = "curPlayingListHistory";
        private const string SongPlayHistoryFileName = "songPlayHistory";

        public static void InitializeHistoryTrack()
        {
            var list = new List<string>();
            if (ApplicationData.Current.LocalSettings.Values["songHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            if (ApplicationData.Current.LocalSettings.Values["songHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            if (ApplicationData.Current.LocalSettings.Values["searchHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            if (ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] == null)
                ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] =
                    JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
            if (ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] =
                    JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        }

        public static void AddNCSongHistory(string songid)
        {
            var list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings.Values["songHistory"]?.ToString() ?? "[]", JsonDefaults.Options);

            list.Remove(songid);
            list.Insert(0, songid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        }

        public static void AddSearchHistory(string Text)
        {
            var list = new List<string>();
            list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString(), JsonDefaults.Options);
            if (!list.Contains(Text))
            {
                list.Insert(0, Text);
            }
            else
            {
                list.RemoveAll(t => t == Text);
                list.Insert(0, Text);
            }

            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        }

        public static void AddSonglistHistory(string playListid)
        {
            var list = JsonSerializer.Deserialize<List<string>>
                (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString(), JsonDefaults.Options);

            list.Remove(playListid);
            list.Insert(0, playListid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
        }

        public static async Task SetcurPlayingListHistory(List<string> songids, int currentIndex)
        {
            var state = new CurPlayingListHistoryState(
                songids,
                currentIndex >= 0 && currentIndex < songids.Count ? currentIndex : -1);

            if (Ioc.Default.GetRequiredService<Setting>().advancedMusicHistoryStorage)
                try
                {
                    var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(SongPlayHistoryFileName,
                        CreationCollisionOption.OpenIfExists);
                    await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(state, JsonDefaults.Options));
                }
                catch
                {
                    // ignored
                }
            else
                //低级音乐存储
                ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] =
                    JsonSerializer.Serialize(state, JsonDefaults.Options);
        }

        public static async Task ClearHistory()
        {
            var list = new List<string>();
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, JsonDefaults.Options);
            ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey] = JsonSerializer.Serialize(new CurPlayingListHistoryState(list, -1), JsonDefaults.Options);
            await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(SongPlayHistoryFileName,
                CreationCollisionOption.OpenIfExists)).DeleteAsync();
        }

        public static async Task<List<NCSong>> GetNCSongHistory()
        {
            try
            {
                var songIds = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["songHistory"].ToString(), JsonDefaults.Options);
                var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.SongDetailApi,
                    new SongDetailRequest()
                    {
                        IdList = songIds
                    });
                if (result.IsSuccess)
                {
                    return result.Value.Songs?.Select(t => t.MapToNcSong()).ToList();
                }
            }
            catch (Exception e)
            {
                Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("储存歌曲记录时发生错误", e.Message);
            }

            return [];
        }

        public static List<string> GetSearchHistory()
        {
            return JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString(), JsonDefaults.Options);
        }

        public static async Task<List<NCSong>> GetcurPlayingListHistory()
        {
            return (await GetCurPlayingListHistoryStateAsync()).Songs;
        }

        public static async Task<CurPlayingListHistoryResult> GetCurPlayingListHistoryStateAsync()
        {
            var retsongs = new List<NCSong>();
            var historyState = await ReadCurPlayingListHistoryStateAsync();
            var trackIds = historyState.SongIds;

            if (trackIds == null || trackIds.Count == 0)
                return new CurPlayingListHistoryResult(retsongs, -1);
            var nowIndex = 0;
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                var json = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.RequestAsync(NeteaseApis.SongDetailApi,
                         new SongDetailRequest()
                         {
                             IdList = nowIds
                         });
                nowIndex++;
                if (json.IsError)
                {
                    Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("加载当前播放失败", json.Error.Message);
                    continue;
                }

                var ncSongs = json.Value.Songs?.Select(t => t.MapToNcSong()).ToList();
                retsongs.AddRange(ncSongs ?? []);
            }

            var currentIndex = historyState.CurrentIndex;
            if (currentIndex < 0 || currentIndex >= retsongs.Count)
                currentIndex = retsongs.Count > 0 ? 0 : -1;

            return new CurPlayingListHistoryResult(retsongs, currentIndex);
        }

        private static async Task<CurPlayingListHistoryState> ReadCurPlayingListHistoryStateAsync()
        {
            string text;
            if (Ioc.Default.GetRequiredService<Setting>().advancedMusicHistoryStorage)
            {
                text = await FileIO.ReadTextAsync(
                    await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(SongPlayHistoryFileName,
                        CreationCollisionOption.OpenIfExists));
            }
            else
            {
                text = ApplicationData.Current.LocalSettings.Values[CurPlayingListHistoryKey]?.ToString() ?? "[]";
            }

            return ParseCurPlayingListHistoryState(text);
        }

        private static CurPlayingListHistoryState ParseCurPlayingListHistoryState(string text)
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

    public sealed record CurPlayingListHistoryResult(List<NCSong> Songs, int CurrentIndex);
}
