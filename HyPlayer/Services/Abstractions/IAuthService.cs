using HyPlayer.Domain.Music;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    /// <summary>清理运行时 Cookie。</summary>
    void ClearRuntimeCookies();

    /// <summary>写入运行时 Cookie。</summary>
    void SetRuntimeCookie(string name, string value);

    /// <summary>尝试使用已保存 Cookie 恢复登录。</summary>
    Task<AuthResult> TryLoadSavedLoginAsync();

    /// <summary>使用手机号或邮箱登录。</summary>
    Task<AuthResult> LoginWithPasswordAsync(string account, string password);

    /// <summary>创建二维码登录 Key。</summary>
    Task<AuthQrKeyResult> CreateQrLoginKeyAsync();

    /// <summary>检查二维码登录状态。</summary>
    Task<AuthQrCheckResult> CheckQrLoginAsync(string key);

    /// <summary>注册当前设备信息。</summary>
    Task<AuthDeviceRegisterResult> RegisterCurrentDeviceAsync();

    /// <summary>完成登录后的认证状态同步。</summary>
    Task<AuthResult> CompleteLoginAsync(bool clearLoginCache);

    /// <summary>登出并清理认证缓存。</summary>
    Task<AuthResult> LogoutAsync();

    /// <summary>通知登录完成（发送 LoginCompletedMessage）</summary>
    void NotifyLoginCompleted();

    /// <summary>红心/取消红心当前播放歌曲</summary>
    void LikeSong();

    /// <summary>红心/取消红心当前播放歌曲</summary>
    Task LikeSongAsync();
}
