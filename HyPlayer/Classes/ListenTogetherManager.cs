#nullable enable
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.ListenTogether;
using HyPlayer.NeteaseApi.ApiContracts.ListenTogether.Dual;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Classes;

public static class ListenTogetherManager
{
    public static bool IsInRoom = false;
    public static RoomInfo? CurrentRoomInfo;

    public delegate void UserChangedEvent(RoomInfo.UserInfo[] users);
    public static event UserChangedEvent? OnUserChanged;



    public static void InitializeListenTogetherManager()
    {
        HyPlayList.OnTimerTicked += TimerTicked;
        HyPlayList.OnPlayItemChange += OnPlayItemChanged;
        HyPlayList.OnSongMoveNext += OnSongMoveNext;
        HyPlayList.OnPlay += OnPlay;
        HyPlayList.OnPause += OnPause;
        HyPlayList.OnManualSeek += OnManualSeek;
        HyPlayList.OnPlayListAddDone += OnPlayListChanged;
        HyPlayList.OnPlayModeChanged += OnPlayModeChanged;
    }

    private static void OnSongMoveNext()
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Next,
                PlayStatus = ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play,
                FormerSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                TargetSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = 0
            };
            _ = Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private static void OnPause()
    {
        if (!IsInRoom || CurrentRoomInfo is null || HyPlayList.Player?.PrimaryAudioInputNode is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Pause,
                PlayStatus = ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Pause,
                FormerSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                TargetSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds
            };
            _ = Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private static void OnPlay()
    {
        if (!IsInRoom || CurrentRoomInfo is null || HyPlayList.Player?.PrimaryAudioInputNode is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Play,
                PlayStatus = ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play,
                FormerSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                TargetSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = (long)HyPlayList.Player.PrimaryAudioInputNode.Position.TotalMilliseconds
            };
            _ = Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }



    private static void OnPlayModeChanged(PlayMode mode)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            var req = new ListenTogetherSyncListReportRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType =
                    ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportCommandType.PlayModeChange,
                PlayMode = HyPlayList.NowPlayType switch
                {
                    PlayMode.DefaultRoll => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode
                        .OrderLoop,
                    PlayMode.SinglePlay => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode
                        .SingleLoop,
                    PlayMode.Shuffled => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode
                        .Random,
                    _ => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode.OrderLoop
                },
                UserId = Common.LoginedUser?.Id!,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                AnchorPosition = HyPlayList.NowPlaying,
                AnchorSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                DisplaySongList = HyPlayList.List.Select(t => t.PlayItem.Id).ToArray()
            };

            if (HyPlayList.NowPlayType == PlayMode.Shuffled)
            {
                if (HyPlayList.ShuffleList.Count > 0)
                {
                    req.RandomSongList = HyPlayList.ShuffleList.Select(t => HyPlayList.List[t].PlayItem.Id).ToArray();
                }
                else
                {
                    req.RandomSongList = HyPlayList.List.Select(t => t.PlayItem.Id).ToArray();
                }
            }

            _ = Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherSyncListReportApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private static void OnPlayListChanged(bool isShuffle = false)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        if (isShuffle) return;
        try
        {
            var req = new ListenTogetherSyncListReportRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType =
                    ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportCommandType.PlayModeChange,
                PlayMode = HyPlayList.NowPlayType switch
                {
                    PlayMode.DefaultRoll => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode
                        .OrderLoop,
                    PlayMode.SinglePlay => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode
                        .SingleLoop,
                    PlayMode.Shuffled => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode
                        .Random,
                    _ => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode.OrderLoop
                },
                UserId = Common.LoginedUser?.Id!,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                AnchorPosition = HyPlayList.NowPlaying,
                AnchorSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                DisplaySongList = HyPlayList.List.Select(t => t.PlayItem.Id).ToArray()
            };

            if (HyPlayList.NowPlayType == PlayMode.Shuffled)
            {
                if (HyPlayList.ShuffleList.Count > 0)
                {
                    req.RandomSongList = HyPlayList.ShuffleList.Select(t => HyPlayList.List[t].PlayItem.Id).ToArray();
                }
                else
                {
                    req.RandomSongList = HyPlayList.List.Select(t => t.PlayItem.Id).ToArray();
                }
            }

            _ = Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherSyncListReportApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private static void OnManualSeek(TimeSpan position)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            Common.NeteaseAPI?.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi,
                new ListenTogetherPlayCommandRequest
                {
                    RoomId = CurrentRoomInfo.RoomId,
                    CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Progress,
                    Progress = (long)position.TotalMilliseconds,
                    PlayStatus = HyPlayList.IsPlaying
                        ? ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play
                        : ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Pause,
                    FormerSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                    TargetSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                    ClientSeq = ++CurrentRoomInfo.ClientSeq
                });
        }
        catch
        {
            // ignore
        }

    }


    private static void OnPlayItemChanged(HyPlayItem playItem)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Play,
                PlayStatus = HyPlayList.IsPlaying
                    ? ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play
                    : ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Pause,
                FormerSongId = HyPlayList.NowPlayingItem.PlayItem.Id,
                TargetSongId = playItem.PlayItem.Id,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = 0
            };
            _ = Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private static int defer = 5;
    private static async void TimerTicked()
    {
        if (!IsInRoom) return;
        try
        {
            defer--;
            if (defer > 0) return;
            defer = 5;

            // check status
            var res = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherStatusApi, new ListenTogetherStatusRequest());

            if (res.IsError)
            {
                Common.AddToTeachingTipLists("获取一起听房间信息失败", res.Error?.Message);
                return;
            }

            if (res.Value?.Data?.IsInRoom is false)
            {
                IsInRoom = false;
            }

            if (res.Value?.Data?.RoomInfo is not null)
            {
                if (CurrentRoomInfo is null)
                {
                    CurrentRoomInfo = new RoomInfo()
                    {
                        RoomId = res.Value.Data.RoomInfo.RoomId!,
                        ClientSeq = 0
                    };
                }
                var roomInfo = res.Value.Data.RoomInfo;
                if (roomInfo != null)
                {
                    if (roomInfo.RoomId != CurrentRoomInfo.RoomId)
                    {
                        Common.AddToTeachingTipLists("一起听状态异常", "房间信息不匹配");
                        return;
                    }

                    if (roomInfo.RoomUsers?.Count != CurrentRoomInfo.Users.Count)
                    {
                        CurrentRoomInfo.Users = roomInfo.RoomUsers?.Select(t => new RoomInfo.UserInfo()
                        {
                            UserId = t.UserId!,
                            Nickname = t.Nickname!,
                            AvatarUrl = t.AvatarUrl!
                        }).ToList() ?? [];
                        OnUserChanged?.Invoke(CurrentRoomInfo.Users.ToArray());
                    }
                }
            }
            else
            {
                Common.AddToTeachingTipLists("获取一起听房间信息失败", "房间信息为空");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public static async Task<bool> CheckRoomCanJoin(string roomId)
    {
        var res = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherRoomCheckApi, new ListenTogetherRoomCheckRequest()
        {
            RoomId = roomId
        });
        if (!res.IsError) return res.Value?.Data?.Joinable is true;
        Common.AddToTeachingTipLists("获取一起听房间信息失败", res.Error?.Message);
        return false;
    }

    public static async Task CreateRoom()
    {
        var res = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ListenTogetherRoomCreateApi,
            new ListenTogetherRoomCreateRequest());
        if (res.IsError)
        {
            Common.AddToTeachingTipLists("创建一起听房间失败", res.Error?.Message);
            return;
        }
        var roomId = res.Value?.Data?.RoomInfo?.RoomId;
        if (roomId == null)
        {
            Common.AddToTeachingTipLists("创建一起听房间失败", "房间ID为空");
            return;
        }

        CurrentRoomInfo = new RoomInfo()
        {
            RoomId = roomId,
            ClientSeq = 0,
            CurrentSongId = HyPlayList.NowPlayingItem.PlayItem.Id
        };
        IsInRoom = true;
    }


}

public class RoomInfo
{
    public required string RoomId;
    public int ClientSeq;
    public RoomInfoPlayMode PlayMode;
    public string[] DisplaySongList { get; set; } = [];
    public string[] RandomSongList { get; set; } = [];
    public string CurrentSongId = "";
    public List<UserInfo> Users = [];

    public class UserInfo
    {
        public required string UserId { get; set; }
        public required string Nickname { get; set; }
        public required string AvatarUrl { get; set; }
    }

    public enum RoomInfoPlayMode
    {
        OrderLoop,
        Random,
        SingleLoop,
    }
}