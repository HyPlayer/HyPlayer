using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;

namespace HyPlayer.Services;

/// <summary>
/// 认证服务实现，管理用户登录状态与收藏数据
/// </summary>
public class AuthService : IAuthService
{
    private readonly PlaybackStateService _state;
    private readonly INotificationService _notification;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly SemaphoreSlim _likeSongGate = new(1, 1);

    public AuthService(
        PlaybackStateService state,
        INotificationService notification,
        IBackgroundTaskRunner taskRunner)
    {
        _state = state;
        _notification = notification;
        _taskRunner = taskRunner;
    }

    /// <inheritdoc />
    public bool IsLoggedIn { get; set; }

    /// <inheritdoc />
    public NCUser? CurrentUser { get; set; }

    /// <inheritdoc />
    public List<string> LikedSongs { get; } = [];

    /// <inheritdoc />
    public List<NCPlayList> MySongLists { get; } = [];

    /// <inheritdoc />
    public Task LoginAsync()
    {
        // NOTE: Login logic partially migrated. Ioc.Default.GetRequiredService<IAuthService>().IsLoggedIn/LoginedUser now delegate to AuthService. Full login flow migration pending.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Logout()
    {
        IsLoggedIn = false;
        CurrentUser = null;
        LikedSongs.Clear();
        MySongLists.Clear();
    }

    /// <inheritdoc />
    public void NotifyLoginCompleted()
    {
        WeakReferenceMessenger.Default.Send(new LoginCompletedMessage());
    }

    /// <inheritdoc />
    public void LikeSong()
    {
        _taskRunner.Forget(LikeSongAsync, "toggle current song like status");
    }

    /// <inheritdoc />
    public async Task LikeSongAsync()
    {
        await _likeSongGate.WaitAsync();
        try
        {
            await LikeSongCoreAsync();
        }
        finally
        {
            _likeSongGate.Release();
        }
    }

    private async Task LikeSongCoreAsync()
    {
        var item = _state.NowPlayingItem;
        if (item == null) return;
        var isLiked = LikedSongs.Contains(item.Id);
        try
        {
            await RetryPolicies.ApiCallPolicy.ExecuteAsync(async () =>
            {
                switch (item.ItemType)
                {
                    case HyPlayItemType.Netease:
                        bool res = await Api.LikeSong(item.Id, !isLiked);
                        if (res)
                        {
                            if (isLiked) LikedSongs.Remove(item.Id);
                            else LikedSongs.Add(item.Id);
                            WeakReferenceMessenger.Default.Send(new SongLikeStatusChangedMessage(!isLiked));
                        }
                        else throw new Exception("红心操作失败");
                        break;
                    case HyPlayItemType.Radio:
                        _notification.ShowMessage("暂不支持红心电台歌曲", "将在后续版本中支持");
                        WeakReferenceMessenger.Default.Send(new SongLikeStatusChangedMessage(!isLiked));
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("红心操作失败", ex.Message);
        }
    }
}
