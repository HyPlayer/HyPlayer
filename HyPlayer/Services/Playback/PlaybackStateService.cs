using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Lyrics;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using Windows.Storage.Streams;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 播放状态中心 — 唯一的播放状态真相来源。
/// <para>
/// 所有播放相关服务写入此对象，ViewModel 通过 PropertyChanged 或 x:Bind 观察。
    /// 高频属性（Position、LyricIndex）和低频播放状态均通过状态属性观察。
/// </para>
/// </summary>
public partial class PlaybackStateService : ObservableObject
{
    /// <summary>当前播放曲目的 Provider 模型视图。</summary>
    [ObservableProperty]
    public partial SingleSongBase? NowPlayingProviderItem { get; set; }

    /// <summary>当前播放曲目的 provider-first UI 快照。</summary>
    [ObservableProperty]
    public partial PlaybackCurrentItemSnapshot? NowPlayingSnapshot { get; set; }

    /// <summary>当前播放位置</summary>
    [ObservableProperty]
    public partial TimeSpan Position { get; set; }

    /// <summary>当前曲目总时长</summary>
    [ObservableProperty]
    public partial TimeSpan Duration { get; set; }

    /// <summary>是否正在播放</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    /// <summary>音量 (0.0 ~ 1.0)</summary>
    [ObservableProperty]
    public partial double Volume { get; set; }

    /// <summary>当前播放策略 Id (seq/sgl/shf/shn/pfm/ltg)</summary>
    [ObservableProperty]
    public partial string ActiveStrategyId { get; set; } = "seq";

    /// <summary>当前过渡策略 Id (dir/xfd/gap)</summary>
    [ObservableProperty]
    public partial string ActiveTransitionId { get; set; } = "dir";

    /// <summary>当前播放索引</summary>
    [ObservableProperty]
    public partial int NowPlayingIndex { get; set; } = -1;

    /// <summary>当前歌词信息</summary>
    [ObservableProperty]
    public partial HyLyricInfo LyricInfo { get; set; } = new();

    /// <summary>当前歌词行索引</summary>
    [ObservableProperty]
    public partial int LyricIndex { get; set; }

    /// <summary>是否处于私人 FM 模式</summary>
    [ObservableProperty]
    public partial bool IsInFm { get; set; }

    /// <summary>当前音质标签</summary>
    [ObservableProperty]
    public partial string QualityTag { get; set; } = string.Empty;

    /// <summary>封面流（异步加载，供 UI 观察刷新）。</summary>
    [ObservableProperty]
    public partial InMemoryRandomAccessStream? CoverStream { get; set; }

    /// <summary>封面流引用（用于 SMTC 等服务层调用，不触发 UI 更新）。</summary>
    public RandomAccessStreamReference? CoverStreamReference { get; set; }

    /// <summary>
    /// Updates the mirrored now-playing state. Provider item is the canonical metadata path.
    /// </summary>
    public void SetNowPlaying(SingleSongBase? providerItem)
    {
        NowPlayingProviderItem = providerItem;
        NowPlayingSnapshot = PlaybackCurrentItemSnapshot.FromProvider(providerItem);
    }

    /// <summary>Clears mirrored now-playing state and resets the playlist index.</summary>
    public void ClearNowPlaying()
    {
        SetNowPlaying(null);
        NowPlayingIndex = -1;
    }
}
