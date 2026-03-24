using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer
{
    public class HistoryManagement
    {
        public static void InitializeHistoryTrack()
        {
            var list = new List<string>();
            if (ApplicationData.Current.LocalSettings.Values["songHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["songHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["searchHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static void AddNCSongHistory(string songid)
        {
            var list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings.Values["songHistory"].ToString(), Common.DefaultOptions);

            list.Remove(songid);
            list.Insert(0, songid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static void AddSearchHistory(string Text)
        {
            var list = new List<string>();
            list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString(), Common.DefaultOptions);
            if (!list.Contains(Text))
            {
                list.Insert(0, Text);
            }
            else
            {
                list.RemoveAll(t => t == Text);
                list.Insert(0, Text);
            }

            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static void AddSonglistHistory(string playListid)
        {
            var list = JsonSerializer.Deserialize<List<string>>
                (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString(), Common.DefaultOptions);

            list.Remove(playListid);
            list.Insert(0, playListid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static async Task SetcurPlayingListHistory(List<string> songids)
        {
            if (Common.Setting.advancedMusicHistoryStorage)
                try
                {
                    var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                        CreationCollisionOption.OpenIfExists);
                    await FileIO.WriteTextAsync(file, string.Join("\r\n", songids));
                }
                catch
                {
                    // ignored
                }
            else
                //低级音乐存储
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonSerializer.Serialize(songids.Count > 100 ? songids.GetRange(0, 100) : songids, Common.DefaultOptions);
        }

        public static async Task ClearHistory()
        {
            var list = new List<string>();
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                CreationCollisionOption.OpenIfExists)).DeleteAsync();
        }

        public static async Task<List<NCSong>> GetNCSongHistory()
        {
            try
            {
                var songIds = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["songHistory"].ToString(), Common.DefaultOptions);
                var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.SongDetailApi,
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
                Common.AddToTeachingTipLists("储存歌曲记录时发生错误", e.Message);
            }

            return [];
        }

        public static List<string> GetSearchHistory()
        {
            return JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString(), Common.DefaultOptions);
        }

        public static async Task<List<NCSong>> GetcurPlayingListHistory()
        {
            var retsongs = new List<NCSong>();
            List<string> trackIds = [];
            if (Common.Setting.advancedMusicHistoryStorage)
                trackIds = [.. (await FileIO.ReadTextAsync(
                    await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                        CreationCollisionOption.OpenIfExists))).Split("\r\n")];
            else
                //低级音乐存储
                trackIds = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["curPlayingListHistory"].ToString(), Common.DefaultOptions) ?? [];

            if (trackIds == null || trackIds.Count == 0)
                return retsongs;
            var nowIndex = 0;
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                var json = await Common.NeteaseAPI?.RequestAsync(NeteaseApis.SongDetailApi,
                         new SongDetailRequest()
                         {
                             IdList = nowIds
                         });
                nowIndex++;
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载当前播放失败", json.Error.Message);
                    continue;
                }

                var ncSongs = json.Value.Songs?.Select(t => t.MapToNcSong()).ToList();
                retsongs.AddRange(ncSongs ?? []);
            }

            return retsongs;
        }
    }
}
