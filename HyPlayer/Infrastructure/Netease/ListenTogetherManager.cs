#nullable enable
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
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
    private readonly IListenTogetherProvidable _listenTogetherProvider;
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
        _listenTogetherProvider = Ioc.Default.GetRequiredService<IListenTogetherProvidable>();
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

        var roomId = await mgr._listenTogetherProvider.CreateListenTogetherRoomAsync(
            mgr._playlist.ProviderItems.ToList());
        if (string.IsNullOrWhiteSpace(roomId))
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("创建一起听房间失败", "房间ID为空");
            _instance = null;
            return;
        }

        mgr.CurrentRoomInfo = new RoomInfo
        {
            RoomId = roomId,
            ClientSeq = 0,
            CurrentSongId = mgr.CurrentProviderSongId
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
        try
        {
            return await Ioc.Default.GetRequiredService<IListenTogetherProvidable>()
                .CanJoinListenTogetherRoomAsync(roomId);
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("获取一起听房间信息失败", ex.Message);
            return false;
        }
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
            var command = new ProviderListenTogetherPlaybackCommand
            {
                CommandId = "play",
                IsPlaying = _state.IsPlaying,
                FormerItemId = CurrentProviderSongId,
                TargetItemId = CurrentProviderSongId,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Position = TimeSpan.Zero
            };
            _ = _listenTogetherProvider.SendListenTogetherPlaybackCommandAsync(CurrentRoomInfo.RoomId, command);
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
            var command = new ProviderListenTogetherPlaybackCommand
            {
                CommandId = "play",
                IsPlaying = true,
                FormerItemId = CurrentProviderSongId,
                TargetItemId = CurrentProviderSongId,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Position = _state.Position
            };
            _ = _listenTogetherProvider.SendListenTogetherPlaybackCommandAsync(CurrentRoomInfo.RoomId, command);
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
            var command = new ProviderListenTogetherPlaybackCommand
            {
                CommandId = "pause",
                IsPlaying = false,
                FormerItemId = CurrentProviderSongId,
                TargetItemId = CurrentProviderSongId,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                Position = _state.Position
            };
            _ = _listenTogetherProvider.SendListenTogetherPlaybackCommandAsync(CurrentRoomInfo.RoomId, command);
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
            _ = _listenTogetherProvider.SendListenTogetherPlaybackCommandAsync(CurrentRoomInfo.RoomId,
                new ProviderListenTogetherPlaybackCommand
                {
                    CommandId = "progress",
                    Position = position,
                    IsPlaying = _state.IsPlaying,
                    FormerItemId = CurrentProviderSongId,
                    TargetItemId = CurrentProviderSongId,
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
            var report = new ProviderListenTogetherQueueReport
            {
                Queue = _playlist.ProviderItems.ToList(),
                PlayModeId = _state.ActiveStrategyId,
                UserId = Ioc.Default.GetRequiredService<IAuthService>().CurrentUser?.Id,
                ClientSeq = ++CurrentRoomInfo.ClientSeq,
                AnchorPosition = _state.NowPlayingIndex,
                AnchorItemId = CurrentProviderSongId
            };

            _ = _listenTogetherProvider.ReportListenTogetherQueueAsync(CurrentRoomInfo.RoomId, report);
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

            var status = await _listenTogetherProvider.GetListenTogetherStatusAsync(CurrentRoomInfo?.RoomId ?? string.Empty);
            if (status is null)
            {
                Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("获取一起听房间信息失败", "房间状态为空");
                return;
            }

            if (!status.IsInRoom)
            {
                IsInRoom = false;
            }

            if (!string.IsNullOrWhiteSpace(status.RoomId))
            {
                if (CurrentRoomInfo is null)
                {
                    CurrentRoomInfo = new RoomInfo
                    {
                        RoomId = status.RoomId,
                        ClientSeq = 0
                    };
                }

                if (status.RoomId != CurrentRoomInfo.RoomId)
                {
                    Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("一起听状态异常", "房间信息不匹配");
                    return;
                }

                if (status.Users.Count != CurrentRoomInfo.Users.Count)
                {
                    CurrentRoomInfo.Users = status.Users.Select(t => new RoomInfo.UserInfo
                    {
                        UserId = t.UserId,
                        Nickname = t.Nickname,
                        AvatarUrl = t.AvatarUrl
                    }).ToList();
                    OnUserChanged?.Invoke(CurrentRoomInfo.Users.ToArray());
                }
            }
            else
            {
                Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("获取一起听房间信息失败", "房间信息为空");
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

    private string CurrentProviderSongId => _state.NowPlayingProviderItem?.ActualId ?? string.Empty;

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
