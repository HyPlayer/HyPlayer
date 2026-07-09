using HyPlayer.Domain.Lyrics;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Features.Lyrics.Services;

/// <summary>
/// 歌词服务，负责歌词加载和逐行同步
/// </summary>
public interface ILyricService
{
    /// <summary>当前歌词信息</summary>
    HyLyricInfo CurrentLyricInfo { get; }

    /// <summary>当前歌词行索引</summary>
    int CurrentLyricIndex { get; }

    /// <summary>歌词偏移量（手动调节）</summary>
    TimeSpan LyricOffset { get; set; }

    /// <summary>
    /// 为指定 Provider 曲目加载歌词。
    /// </summary>
    Task LoadLyricsAsync(SingleSongBase providerItem, CancellationToken ct = default);

    /// <summary>
    /// 导入本地歌词并写入当前播放歌曲的歌词状态/缓存。
    /// </summary>
    Task<HyLyricInfo?> ImportLyricsAsync(StorageFile lyricFile, SingleSongBase? currentSong, CancellationToken ct = default);

    /// <summary>
    /// 根据播放位置更新当前歌词行
    /// </summary>
    void Tick(TimeSpan position);
}
