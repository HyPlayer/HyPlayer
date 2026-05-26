using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 播放通知服务，负责 SMTC（系统媒体控制）、磁贴更新、封面加载、Last.FM Scrobble
/// </summary>
public interface IPlaybackNotificationService
{
    /// <summary>
    /// 曲目切换时调用（更新 SMTC、磁贴、封面等）
    /// </summary>
    Task OnTrackChangedAsync(SingleSongBase providerItem);

    /// <summary>
    /// 刷新封面
    /// </summary>
    Task RefreshCoverAsync(SingleSongBase providerItem);

    /// <summary>
    /// 曲目播放结束时 Scrobble
    /// </summary>
    Task ScrobbleAsync(SingleSongBase providerItem);
}
