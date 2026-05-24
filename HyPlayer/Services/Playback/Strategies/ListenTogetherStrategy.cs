using HyPlayer.Infrastructure.Netease;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 一起听策略：播放行为由服务器驱动。
/// <para>
/// 曲目自然结束时停止播放，等待服务器指令。
/// GetNext/GetPrevious 返回 null，因为切歌由服务器控制。
/// 当服务器发送 "next song" 指令时，<see cref="ListenTogetherManager"/> 调用
/// <c>_playlist.MoveToAsync(item)</c> 直接跳转。
/// </para>
/// </summary>
public sealed class ListenTogetherStrategy : IPlayStrategy
{
    /// <summary>
    /// 当前房间信息，由外部设置
    /// </summary>
    public RoomInfo? CurrentRoomInfo { get; set; }

    /// <inheritdoc />
    public string Id => "ltg";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        // 检查 Manager 是否设置了服务器指定的下一首索引
        var mgr = ListenTogetherManager.Instance;
        if (mgr?.ServerNextIndex is { } idx)
        {
            mgr.ServerNextIndex = null; // 消费后清除
            return idx >= 0 && idx < ctx.QueueCount ? idx : null;
        }

        return null;
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx) => null;

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx) { }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.Stop;
}
