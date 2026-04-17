using System;
using HyPlayer.Classes;

namespace HyPlayer.Services.Playback.Messages;

/// <summary>
/// 曲目自然播放结束
/// </summary>
public record TrackEndedMessage(HyPlayItem Item);

/// <summary>
/// 播放列表内容发生变化（增删、清空）
/// </summary>
public record PlaylistChangedMessage(bool IsShuffleTrigger = false);

/// <summary>
/// 用户手动拖动进度条
/// </summary>
public record SeekRequestedMessage(TimeSpan Position);

/// <summary>
/// 登录完成
/// </summary>
public record LoginCompletedMessage;

/// <summary>
/// 歌词颜色切换
/// </summary>
public record LyricColorChangedMessage;

/// <summary>
/// 红心/喜欢状态变化
/// </summary>
public record SongLikeStatusChangedMessage(bool IsLiked);

/// <summary>
/// 封面加载完成
/// </summary>
public record CoverChangedMessage(HyPlayItem Item);

/// <summary>
/// 歌词加载完成
/// </summary>
public record LyricLoadedMessage(HyLyricInfo Info);

/// <summary>
/// 音质信息更新
/// </summary>
public record QualityTagChangedMessage(string Tag);

/// <summary>
/// 歌词行索引变化（由 LyricService.Tick 触发）
/// </summary>
public record LyricIndexChangedMessage(int Index);
