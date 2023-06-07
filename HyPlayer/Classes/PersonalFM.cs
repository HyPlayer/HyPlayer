#region

using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System;
using System.Threading.Tasks;

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
                if (HyPlayList.List.Count > 2)
                    HyPlayList.RemoveAllSong();
                else if (HyPlayList.List.Count > 0) HyPlayList.List.RemoveAt(0);
                if (HyPlayList.List.Count < 1)
                {
                    //只有一首需要请求下一首
                    var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.PersonalFm);
                    var song1 = json["data"][0];
                    var song2 = json["data"][1];
                    var item1 = HyPlayList.AppendNcSong(NCSong.CreateFromJson(song1));
                    var item2 = HyPlayList.AppendNcSong(NCSong.CreateFromJson(song2));
                    item1.ItemType = HyPlayItemType.Netease;
                    item2.ItemType = HyPlayItemType.Netease;
                    json.RemoveAll();
                }
                await HyPlayList.SongMoveTo(0);
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