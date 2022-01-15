#region

using System;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;

#endregion

namespace HyPlayer.Classes
{
    internal static class PersonalFM
    {
        public static void InitPersonalFM()
        {
            LoadNextFM();
            HyPlayList.OnSongMoveNext += HyPlayList_OnSongMoveNext;
            HyPlayList.OnMediaEnd += HyPlayList_OnMediaEnd;
            Common.GLOBAL["PERSONALFM"] = "true";
        }

        public static void ExitFm()
        {
            HyPlayList.OnSongMoveNext -= HyPlayList_OnSongMoveNext;
            HyPlayList.OnMediaEnd -= HyPlayList_OnMediaEnd;
            Common.GLOBAL["PERSONALFM"] = "false";
            HyPlayList.RemoveAllSong();
        }

        private static void HyPlayList_OnMediaEnd(HyPlayItem hpi)
        {
            if (Common.GLOBAL["PERSONALFM"].ToString() == "true")
                LoadNextFM();
        }

        public static async void LoadNextFM()
        {
            try
            {
                if (HyPlayList.List.Count > 2)
                    HyPlayList.RemoveAllSong();
                else if (HyPlayList.List.Count > 0) HyPlayList.List.RemoveAt(0);
                if (HyPlayList.List.Count < 1)
                {
                    //只有一首需要请求下一首
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PersonalFm);
                    var song1 = json["data"][0];
                    var song2 = json["data"][1];
                    var item1 = HyPlayList.AppendNcSong(NCSong.CreateFromJson(song1));
                    var item2 = HyPlayList.AppendNcSong(NCSong.CreateFromJson(song2));
                    item1.ItemType = HyPlayItemType.Netease;
                    item2.ItemType = HyPlayItemType.Netease;
                }

                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(0);
                Common.GLOBAL["PERSONALFM"] = "true";
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }
        }

        private static void HyPlayList_OnSongMoveNext()
        {
            if (Common.GLOBAL["PERSONALFM"].ToString() == "true")
                LoadNextFM();
        }
    }
}