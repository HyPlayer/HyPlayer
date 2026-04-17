using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 一起听策略：播放行为由服务器驱动。
/// 曲目自然结束时停止播放，等待服务器指令。
/// GetNext/GetPrevious 返回 null，因为切歌由服务器控制。
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
    public int? GetNext(PlayStrategyContext ctx) => null;

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx) => null;

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx) { }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx) => PlayStrategyAction.Stop;
}
