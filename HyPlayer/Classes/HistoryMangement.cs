using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Classes
{
    internal class HistoryManagement
    {
        public static void InitializeHistoryTrack()
        {
            var list = new List<string>();

            if (ApplicationData.Current.LocalSettings.Values["songHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["songHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["searchHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
        }

        public static void AddNCSongHistory(string songid)
        {
            var list = new List<string>();
            list = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["songHistory"].ToString());

            list.Remove(songid);
            list.Insert(0, songid);
            if (list.Count >= 300)
                list.RemoveRange(9, list.Count - 300);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
        }

        public static void AddSearchHistory(string Text)
        {
            var list = new List<string>();
            list = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString());
            if (!list.Contains(Text))
            {
                list.Insert(0, Text);
            }
            else
            {
                list.RemoveAll(t => t == Text);
                list.Insert(0, Text);
            }

            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
        }

        public static void AddSonglistHistory(string playListid)
        {
            var list = new List<string>();
            list = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["songlistHistory"].ToString());

            list.Remove(playListid);
            list.Insert(0, playListid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
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
                    JsonConvert.SerializeObject(songids.Count > 100 ? songids.GetRange(0, 100) : songids);
        }

        public static async Task ClearHistory()
        {
            var list = new List<string>();
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
            await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                CreationCollisionOption.OpenIfExists)).DeleteAsync();
        }

        public static async Task<List<NCSong>> GetNCSongHistory()
        {
            var retsongs = new List<NCSong>();
            try
            {
                var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.SongDetail,
                    new Dictionary<string, object>
                    {
                        ["ids"] = string.Join(",",
                            JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                                .Values["songHistory"].ToString()))
                    });
                var history = json["songs"].ToArray().Select(NCSong.CreateFromJson).ToList();
                json.RemoveAll();
                return history;
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }

            return new List<NCSong>();
        }

        public static async Task<List<NCPlayList>> GetSonglistHistory()
        {
            var i = 0;
            var queries = new Dictionary<string, object>();
            foreach (var plid in JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                         .Values["songlistHistory"].ToString()))
                queries["/api/v6/playlist/detail" + new string('/', i++)] = JsonConvert.SerializeObject(
                    new Dictionary<string, object>
                    {
                        ["id"] = plid,
                        ["n"] = 100000,
                        ["s"] = 8
                    });
            if (queries.Count == 0) return new List<NCPlayList>();
            var ret = new List<NCPlayList>();
            try
            {
                var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.Batch, queries);

                for (var k = 0; k < json.Count - 1; k++)
                    ret.Add(NCPlayList.CreateFromJson(
                        json["/api/v6/playlist/detail" + new string('/', k)]["playlist"]));
                json.RemoveAll();
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }

            return ret;
        }

        public static List<string> GetSearchHistory()
        {
            return JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString());
        }

        public static async Task<List<NCSong>> GetcurPlayingListHistory()
        {
            var retsongs = new List<NCSong>();
            List<string> trackIds = new();
            if (Common.Setting.advancedMusicHistoryStorage)
                trackIds = (await FileIO.ReadTextAsync(
                    await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                        CreationCollisionOption.OpenIfExists))).Split("\r\n").ToList();
            else
                //低级音乐存储
                trackIds = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["curPlayingListHistory"].ToString()) ?? new List<string>();

            if (trackIds == null || string.IsNullOrEmpty(trackIds.FirstOrDefault()))
                return retsongs;
            var nowIndex = 0;
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                try
                {
                    var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object> { ["ids"] = string.Join(",", nowIds) });
                    nowIndex++;
                    var i = 0;
                    var ncSongs = (json["songs"] ?? new JArray()).Select(t =>
                    {
                        if (json["privileges"] == null) return null;
                        if (json["privileges"].ToList()[i++]["st"]?.ToString() == "0")
                            return NCSong.CreateFromJson(t);

                        return null;
                    }).ToList();
                    ncSongs.RemoveAll(t => t == null);
                    retsongs.AddRange(ncSongs);
                    json.RemoveAll();
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }
            }

            return retsongs;
        }
    }
}
