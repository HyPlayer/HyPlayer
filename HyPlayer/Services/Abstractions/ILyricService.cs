using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 歌词服务，负责歌词加载和逐行同步
/// </summary>
public interface ILyricService
{
    event EventHandler<LyricLoadedEventArgs>? LyricLoaded;
    event EventHandler<LyricIndexChangedEventArgs>? LyricIndexChanged;

    /// <summary>当前歌词信息</summary>
    HyLyricInfo CurrentLyricInfo { get; }

    /// <summary>当前歌词行索引</summary>
    int CurrentLyricIndex { get; }

    /// <summary>歌词偏移量（手动调节）</summary>
    TimeSpan LyricOffset { get; set; }

    /// <summary>
    /// 为指定曲目加载歌词
    /// </summary>
    Task LoadLyricsAsync(HyPlayItem item, CancellationToken ct = default);

    /// <summary>
    /// 为指定曲目加载歌词，优先使用 Provider 曲目模型。
    /// </summary>
    Task LoadLyricsAsync(HyPlayItem item, SingleSongBase? providerItem, CancellationToken ct = default);

    /// <summary>
    /// 根据播放位置更新当前歌词行
    /// </summary>
    void Tick(TimeSpan position);
}
