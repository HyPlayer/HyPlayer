namespace HyPlayer.Services.Messages;

/// <summary>
/// 歌单集合变更通知（创建/删除/公开/收藏歌单后触发）。
/// 仅用于侧边栏刷新，与 Playback.Messages.PlaylistChangedMessage（PlayBar 专用）隔离。
/// </summary>
public record PlaylistCollectionChangedMessage();
