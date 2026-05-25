using HyPlayer.Domain.Music;
using System;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 播放控制服务，封装底层 IPlayer 操作，协调播放状态更新
/// </summary>
public interface IPlaybackControlService
{
    event EventHandler<SeekRequestedEventArgs>? SeekRequested;

    /// <summary>是否正在播放</summary>
    bool IsPlaying { get; }

    /// <summary>当前播放位置</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// 音量 (0.0 ~ 1.0)
    /// </summary>
    double Volume { get; set; }

    /// <summary>跳转到指定位置</summary>
    Task SeekAsync(TimeSpan target);

    /// <summary>播放</summary>
    void Play();

    /// <summary>暂停</summary>
    void Pause();

    /// <summary>切换播放/暂停</summary>
    void TogglePlayPause();

    /// <summary>
    /// 加载曲目媒体源并播放
    /// </summary>
    /// <param name="item">要播放的曲目</param>
    /// <param name="setAsPrimary">是否设为主播放源</param>
    /// <param name="autoPlay">是否自动开始播放</param>
    /// <param name="removeCurrentSongs">是否移除当前播放的所有曲目</param>
    Task LoadAndPlayAsync(HyPlayItem item, bool setAsPrimary = true, bool autoPlay = true, bool removeCurrentSongs = true);

    /// <summary>
    /// 初始化播放器（AudioGraph 等底层资源）
    /// </summary>
    Task InitializeAsync();
}
