#nullable enable
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.ListenTogether;
using HyPlayer.NeteaseApi.ApiContracts.ListenTogether.Dual;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Infrastructure.Netease;

/// <summary>
/// 一起听管理器。
/// <para>
/// 保留静态入口 <see cref="CreateRoom"/> / <see cref="JoinRoom"/> / <see cref="LeaveRoom"/>
/// 以兼容现有调用方，内部通过 <see cref="Ioc.Default"/> 解析 DI 服务，不再直接引用 HyPlayList。
/// </para>
/// </summary>
internal sealed class ListenTogetherManager
{
    private static ListenTogetherManager? _instance;

    private readonly IPlaylistService _playlist;
    private readonly PlaybackStateService _state;
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly IGlobalTimerService _globalTimer;

    public bool IsInRoom { get; private set; }
    public RoomInfo? CurrentRoomInfo { get; private set; }

    public delegate void UserChangedEvent(RoomInfo.UserInfo[] users);
    public event UserChangedEvent? OnUserChanged;

    /// <summary>服务器指定的下一首索引（供 ListenTogetherStrategy 读取）</summary>
    internal int? ServerNextIndex { get; set; }

    private int _heartbeatDefer = 5;

    private ListenTogetherManager()
    {
        _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    }

    // ---------------------------------------------------------------
    //  Static entry points (backward-compatible)
    // ---------------------------------------------------------------

    /// <summary>当前实例（供 ListenTogetherStrategy 访问）</summary>
    internal static ListenTogetherManager? Instance => _instance;

    /// <summary>是否在房间中（静态便捷属性）</summary>
    public static bool IsInRoomStatic => _instance?.IsInRoom ?? false;

    /// <summary>当前房间信息（静态便捷属性）</summary>
    public static RoomInfo? CurrentRoomInfoStatic => _instance?.CurrentRoomInfo;

    public static async Task CreateRoom()
    {
        CleanupInstance();

        var mgr = new ListenTogetherManager();
        _instance = mgr;

        var res = await mgr._api.RequestAsync(NeteaseApis.ListenTogetherRoomCreateApi,
            new ListenTogetherRoomCreateRequest());
        if (res.IsError)
        {
            Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("创建一起听房间失败", res.Error?.Message));
            _instance = null;
            return;
        }

        var roomId = res.Value?.Data?.RoomInfo?.RoomId;
        if (roomId == null)
        {
            Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("创建一起听房间失败", "房间ID为空"));
            _instance = null;
            return;
        }

        mgr.CurrentRoomInfo = new RoomInfo
        {
            RoomId = roomId,
            ClientSeq = 0,
            CurrentSongId = mgr._state.NowPlayingItem?.Id ?? ""
        };
        mgr.IsInRoom = true;

        mgr.RegisterMessages();
    }

    public static async Task<bool> JoinRoom(string roomId)
    {
        CleanupInstance();

        var mgr = new ListenTogetherManager();
        _instance = mgr;

        var canJoin = await CheckRoomCanJoin(roomId);
        if (!canJoin)
        {
            _instance = null;
            return false;
        }

        mgr.CurrentRoomInfo = new RoomInfo
        {
            RoomId = roomId,
            ClientSeq = 0
        };
        mgr.IsInRoom = true;

        mgr.RegisterMessages();
        return true;
    }

    public static void LeaveRoom()
    {
        CleanupInstance();
    }

    public static async Task<bool> CheckRoomCanJoin(string roomId)
    {
        var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        var res = await api.RequestAsync(NeteaseApis.ListenTogetherRoomCheckApi,
            new ListenTogetherRoomCheckRequest { RoomId = roomId });
        if (!res.IsError) return res.Value?.Data?.Joinable is true;
        Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("获取一起听房间信息失败", res.Error?.Message));
        return false;
    }

    /// <summary>
    /// 服务器指令：跳转到指定曲目
    /// </summary>
    internal async Task ServerMoveToAsync(HyPlayItem item)
    {
        await _playlist.MoveToAsync(item);
    }

    // ---------------------------------------------------------------
    //  Messenger handlers
    // ---------------------------------------------------------------

    private void OnSecondTick(object? sender, EventArgs e)
    {
        HeartbeatTick();
    }

    private void OnPlaybackStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackStateService.NowPlayingItem) && _state.NowPlayingItem is { } item)
            OnPlayItemChanged(item);
        else if (e.PropertyName == nameof(PlaybackStateService.IsPlaying) && _state.IsPlaying)
            OnPlay();
        else if (e.PropertyName == nameof(PlaybackStateService.IsPlaying))
            OnPause();
    }

    private void OnSeekRequested(object? sender, SeekRequestedEventArgs message)
    {
        OnManualSeek(message.Position);
    }

    private void OnPlaylistChanged(object? sender, PlaylistChangedEventArgs message)
    {
        OnPlayListChanged(message.IsShuffleTrigger);
    }

    // ---------------------------------------------------------------
    //  Event handlers (instance methods, no HyPlayList refs)
    // ---------------------------------------------------------------

    private void OnPlayItemChanged(HyPlayItem playItem)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Play,
                PlayStatus = _state.IsPlaying
                    ? ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play
                    : ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Pause,
                FormerSongId = _state.NowPlayingItem?.Id ?? "",
                TargetSongId = playItem.Id,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = 0
            };
            _ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private void OnPlay()
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Play,
                PlayStatus = ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play,
                FormerSongId = _state.NowPlayingItem?.Id ?? "",
                TargetSongId = _state.NowPlayingItem?.Id ?? "",
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = (long)_state.Position.TotalMilliseconds
            };
            _ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private void OnPause()
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            var req = new ListenTogetherPlayCommandRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Pause,
                PlayStatus = ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Pause,
                FormerSongId = _state.NowPlayingItem?.Id ?? "",
                TargetSongId = _state.NowPlayingItem?.Id ?? "",
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Progress = (long)_state.Position.TotalMilliseconds
            };
            _ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private void OnManualSeek(TimeSpan position)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        try
        {
            _ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi,
                new ListenTogetherPlayCommandRequest
                {
                    RoomId = CurrentRoomInfo.RoomId,
                    CommandType = ListenTogetherPlayCommandRequest.ListenTogetherPlayCommandRequestCommandType.Progress,
                    Progress = (long)position.TotalMilliseconds,
                    PlayStatus = _state.IsPlaying
                        ? ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Play
                        : ListenTogetherHeartBeatRequest.ListenTogetherPlayStatus.Pause,
                    FormerSongId = _state.NowPlayingItem?.Id ?? "",
                    TargetSongId = _state.NowPlayingItem?.Id ?? "",
                    ClientSeq = ++CurrentRoomInfo.ClientSeq
                });
        }
        catch
        {
            // ignored
        }
    }

    private void OnPlayListChanged(bool isShuffle = false)
    {
        if (!IsInRoom || CurrentRoomInfo is null) return;
        if (isShuffle) return;
        try
        {
            var strategyId = _state.ActiveStrategyId;
            var req = new ListenTogetherSyncListReportRequest
            {
                RoomId = CurrentRoomInfo.RoomId,
                CommandType =
                    ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportCommandType.PlayModeChange,
                PlayMode = MapStrategyToSyncPlayMode(strategyId),
                UserId = Ioc.Default.GetRequiredService<IAuthService>().CurrentUser?.Id!,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                AnchorPosition = _state.NowPlayingIndex,
                AnchorSongId = _state.NowPlayingItem?.Id ?? "",
                DisplaySongList = _playlist.Items.Select(t => t.Id).ToArray()
            };

            _ = _api.RequestAsync(NeteaseApis.ListenTogetherSyncListReportApi, req);
        }
        catch
        {
            // ignored
        }
    }

    private async void HeartbeatTick()
    {
        if (!IsInRoom) return;
        try
        {
            _heartbeatDefer--;
            if (_heartbeatDefer > 0) return;
            _heartbeatDefer = 5;

            var res = await _api.RequestAsync(NeteaseApis.ListenTogetherStatusApi,
                new ListenTogetherStatusRequest());

            if (res.IsError)
            {
                Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("获取一起听房间信息失败", res.Error?.Message));
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
                    CurrentRoomInfo = new RoomInfo
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
                        Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("一起听状态异常", "房间信息不匹配"));
                        return;
                    }

                    if (roomInfo.RoomUsers?.Count != CurrentRoomInfo.Users.Count)
                    {
                        CurrentRoomInfo.Users = roomInfo.RoomUsers?.Select(t => new RoomInfo.UserInfo
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
                Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("获取一起听房间信息失败", "房间信息为空"));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    private void RegisterMessages()
    {
        _globalTimer.SecondTick += OnSecondTick;
        _state.PropertyChanged += OnPlaybackStatePropertyChanged;
        Ioc.Default.GetRequiredService<IPlaybackControlService>().SeekRequested += OnSeekRequested;
        _playlist.PlaylistChanged += OnPlaylistChanged;
    }

    private static void CleanupInstance()
    {
        if (_instance is not null)
        {
            _instance._globalTimer.SecondTick -= _instance.OnSecondTick;
            _instance._state.PropertyChanged -= _instance.OnPlaybackStatePropertyChanged;
            Ioc.Default.GetRequiredService<IPlaybackControlService>().SeekRequested -= _instance.OnSeekRequested;
            _instance._playlist.PlaylistChanged -= _instance.OnPlaylistChanged;
            _instance.IsInRoom = false;
            _instance = null;
        }
    }

    private static ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode MapStrategyToSyncPlayMode(
        string strategyId)
    {
        return strategyId switch
        {
            "sgl" => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode.SingleLoop,
            "shn" => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode.Random,
            _ => ListenTogetherSyncListReportRequest.ListenTogetherSyncListReportPlayMode.OrderLoop
        };
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
