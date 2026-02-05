#region

using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using System;
using System.Threading.Tasks;

#endregion

namespace HyPlayer.Classes;

internal static class PersonalFM
{
    private static bool _isNew = true;

    public static void InitPersonalFM()
    {
        HyPlayList.ChangePlayMode(PlayMode.DefaultRoll);
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
                    var finalIndex = Math.Max(HyPlayList.List.Count - 1, 0);
                    if (!Common.Setting.useAiDj)
                    {
                        {
                            // 预加载下一首
                            var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PersonalFmApi);
                            if (result.IsError || result.Value?.Items?.Length is not > 0)
                            {
                                Common.AddToTeachingTipLists("加载私人 FM错误", result.Error?.Message ?? "未知错误");
                                return;
                            }

                            foreach (var personalFmDataItem in result.Value.Items)
                            {
                                HyPlayList.AppendNcSong(personalFmDataItem.MapToNcSong());
                            }
                        }
                    }
                    else
                    {
                        // AIDJ
                        // 预加载后续内容
                        var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.AiDjContentRcmdInfoApi,
                            new AiDjContentRcmdInfoRequest
                            {
                                IsNewToAidj = _isNew
                            });
                        _isNew = false;
                        if (result.IsError || result.Value?.Data?.AiDjResources?.Length is not > 0)
                        {
                            Common.AddToTeachingTipLists("加载私人 FM错误", result.Error?.Message ?? "未知错误");
                            return;
                        }

                        foreach (var aiDjContentRcmdInfoResource in result.Value.Data.AiDjResources)
                        {
                            if (aiDjContentRcmdInfoResource is AiDjContentRcmdInfoResponse.AiDjContentRcmdInfoData.AiDjContentRcmdAudioResource audioValue)
                            {
                                foreach (var audioItem in audioValue.Value?.Audio ?? [])
                                {
                                    var playItem = new HyPlayItem()
                                    {
                                        ItemType = HyPlayItemType.Netease,
                                        PlayItem = new PlayItem
                                        {
                                            Album = new NCAlbum
                                            {
                                                AlbumType = HyPlayItemType.Netease,
                                                Alias = "私人 DJ",
                                                Cover =
                                                    "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                                Description = "私人 DJ",
                                                Id = "126368130",
                                                Name = "私人 DJ 推荐语"
                                            },
                                            Artist =
                                            [
                                                new NCArtist()
                                                {
                                                    Alias = "私人 DJ",
                                                    Avatar =
                                                        "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                                    Id = "1",
                                                    Name = "私人 DJ",
                                                    TranslatedName = null,
                                                    Type = HyPlayItemType.Netease
                                                }
                                            ],
                                            Bitrate = 0,
                                            CDName = null,
                                            Id = "-1",
                                            IsLocalFile = false,
                                            LengthInMilliseconds = audioItem.Duration,
                                            Name = "私人 DJ 推荐语",
                                            InfoTag = "私人 DJ",
                                            Type = HyPlayItemType.Netease,
                                            Url = audioItem.Url,
                                            Size = audioItem.Size ?? 0
                                        }
                                    };
                                    HyPlayList.List.Add(playItem);
                                }
                            }
                            else if (aiDjContentRcmdInfoResource is AiDjContentRcmdInfoResponse.AiDjContentRcmdInfoData.AiDjContentRcmdAudioSong songValue)
                            {
                                var ncSong = songValue.Value?.SongName?.MapToNcSong();
                                if (ncSong is not null)
                                {
                                    HyPlayList.AppendNcSong(ncSong);
                                }
                            }
                        }
                    }

                    HyPlayList.SongAppendDone();
                    var item = HyPlayList.List[finalIndex];
                    HyPlayList.SongMoveTo(item);
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