#region

using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

#endregion

namespace HyPlayer.Classes;

internal static class PersonalFM
{
    public static void InitPersonalFM()
    {
        HyPlayList.NowPlayType = PlayMode.DefaultRoll;
        HyPlayList.OnSongMoveNext += HyPlayList_OnSongMoveNext;
        HyPlayList.OnMediaEnd += HyPlayList_OnMediaEnd;
        Common.IsInFm = true;
        HyPlayList.RemoveAllSong();
        LoadNextFM();
    }

    public static void ExitFm()
    {
        HyPlayList.OnSongMoveNext -= HyPlayList_OnSongMoveNext;
        HyPlayList.OnMediaEnd -= HyPlayList_OnMediaEnd;
        Common.IsInFm = false;
        HyPlayList.RemoveAllSong();
    }

    private static async void HyPlayList_OnMediaEnd(HyPlayItem hpi)
    {
        if (Common.IsInFm)
            await LoadNextFM();
    }

    public static Task LoadNextFM()
    {
        return Task.Run(async () =>
        {
            try
            {
                if (HyPlayList.NowPlaying + 1 >= HyPlayList.List.Count)
                {
                    var appendedIndex = HyPlayList.List.Count;
                    if (!Common.Setting.useAiDj)
                    {
                        {
                            // 预加载下一首
                            var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.PersonalFm);                            
                           foreach (var jToken in json["data"]?.Children() ?? new JEnumerable<JToken>())
                           {
                               HyPlayList.List.Add(HyPlayList.LoadNcSong(NCSong.CreateFromJson(jToken)));
                           }
                            json.RemoveAll();
                        }
                    }
                    else
                    {
                        // AIDJ
                        // 预加载后续内容
                        var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.AiDjContent);
                        foreach (var aidjResource in json["data"]?["aiDjResources"]?.Children() ??
                                                     new JEnumerable<JToken>())
                        {
                            if (aidjResource["type"]?.ToString() == "audio")
                            {
                                foreach (var audioItem in aidjResource["value"]?["audioList"]?.Children() ??
                                                          new JEnumerable<JToken>())
                                {
                                    var playItem = new HyPlayItem()
                                                   {
                                                       ItemType = HyPlayItemType.Netease,
                                                       PlayItem = new PlayItem
                                                                  {
                                                                      Album = new NCAlbum
                                                                              {
                                                                                  AlbumType = HyPlayItemType.Netease,
                                                                                  alias = "私人 DJ",
                                                                                  cover =
                                                                                      "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                                                                  description = "私人 DJ",
                                                                                  id = "126368130",
                                                                                  name = "私人 DJ 推荐语"
                                                                              },
                                                                      Artist = new List<NCArtist>()
                                                                               {
                                                                                   new NCArtist
                                                                                   {
                                                                                       alias = "私人 DJ",
                                                                                       avatar =
                                                                                           "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                                                                       id = "1",
                                                                                       name = "私人 DJ",
                                                                                       transname = null,
                                                                                       Type = HyPlayItemType.Netease
                                                                                   }
                                                                               },
                                                                      Bitrate = 0,
                                                                      CDName = null,
                                                                      Id = "-1",
                                                                      IsLocalFile = false,
                                                                      LengthInMilliseconds =
                                                                          audioItem["validTime"]
                                                                              ?.ToObject<long>() ??
                                                                          114514,
                                                                      Name = "私人 DJ 推荐语",
                                                                      Tag = "私人 DJ",
                                                                      Type = HyPlayItemType.Netease,
                                                                      Url = audioItem["audioUrl"]?.ToString()
                                                                  }
                                                   };
                                    HyPlayList.List.Add(playItem);
                                }
                            }

                            if (aidjResource["type"]?.ToString() == "song")
                            {
                                var ncSong = NCSong.CreateFromJson(aidjResource["value"]?["songData"]);
                                if (ncSong is not null)
                                {
                                    HyPlayList.AppendNcSong(ncSong);
                                }
                            }
                        }
                        json.RemoveAll();
                    }
                    HyPlayList.SongAppendDone();
                    HyPlayList.SongMoveTo(appendedIndex);
                }

                Common.IsInFm = true;
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }
        });
    }

    private static void HyPlayList_OnSongMoveNext()
    {
        if (Common.IsInFm)
            LoadNextFM();
    }
}