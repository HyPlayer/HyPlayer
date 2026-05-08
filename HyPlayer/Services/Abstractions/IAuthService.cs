using System.Collections.Generic;
using System.Threading.Tasks;
using HyPlayer.Classes;

using HyPlayer.Services.Abstractions;
namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 认证服务，管理用户登录状态与收藏数据
/// </summary>
public interface IAuthService
{
    /// <summary>是否已登录</summary>
    bool IsLoggedIn { get; set; }

    /// <summary>当前登录用户</summary>
    NCUser? CurrentUser { get; set; }

    /// <summary>喜欢的歌曲 ID 列表</summary>
    List<string> LikedSongs { get; }

    /// <summary>用户歌单列表</summary>
    List<NCPlayList> MySongLists { get; }

    /// <summary>执行登录流程</summary>
    Task LoginAsync();

    /// <summary>登出</summary>
    void Logout();

    /// <summary>通知登录完成（发送 LoginCompletedMessage）</summary>
    void NotifyLoginCompleted();

    /// <summary>红心/取消红心当前播放歌曲</summary>
    void LikeSong();
}
