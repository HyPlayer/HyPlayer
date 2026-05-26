using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 播放列表服务，管理播放列表内容和播放顺序。
/// 内部组合 <see cref="IPlayStrategy"/>（决定下一首）和 <see cref="ITrackTransition"/>（决定怎么过渡）。
/// </summary>
public interface IPlaylistService
{
    event EventHandler<PlaylistChangedEventArgs>? PlaylistChanged;

    /// <summary>当前播放列表的旧 UI 投影视图（只读快照）</summary>
    IReadOnlyList<HyPlayItem> LegacyItemsSnapshot { get; }

    /// <summary>当前播放列表的 Provider 模型视图（只读快照）</summary>
    IReadOnlyList<SingleSongBase> ProviderItems { get; }

    /// <summary>当前播放列表的 Provider 模型位置对齐视图（本地/旧项可为 null）</summary>
    IReadOnlyList<SingleSongBase?> ProviderQueueSnapshot { get; }

    /// <summary>当前播放队列长度</summary>
    int QueueCount { get; }

    /// <summary>当前播放索引</summary>
    int NowPlayingIndex { get; }

    /// <summary>当前播放曲目的 Provider 模型视图</summary>
    SingleSongBase? NowPlayingProviderItem { get; }

    /// <summary>当前播放策略 Id</summary>
    string ActiveStrategyId { get; }

    /// <summary>当前过渡策略 Id</summary>
    string ActiveTransitionId { get; }

    /// <summary>是否处于私人 FM 模式</summary>
    bool IsInFm { get; }

    /// <summary>播放来源标识（用于历史记录等）</summary>
    string PlaySourceId { get; set; }

    /// <summary>追加本地文件旧队列投影</summary>
    void AppendLocalItem(HyPlayItem item, int position = -1);

    /// <summary>追加 Provider 曲目</summary>
    void AppendItem(ProvidableItemBase item, int position = -1);

    /// <summary>设置 Provider 曲目在旧播放列表投影视图中的显示标签</summary>
    void SetItemInfoTag(ProvidableItemBase item, string infoTag);

    /// <summary>批量加载本地文件旧队列投影</summary>
    void AppendLocalItems(IEnumerable<HyPlayItem> items, bool clearFirst = false);

    /// <summary>批量追加 Provider 曲目</summary>
    void AppendItems(IEnumerable<ProvidableItemBase> items, bool clearFirst = false);

    /// <summary>批量追加 Provider 单曲</summary>
    void AppendItems(IEnumerable<SingleSongBase> items, bool clearFirst = false);

    /// <summary>移除指定位置的曲目</summary>
    void RemoveAt(int index);

    /// <summary>清空播放列表</summary>
    void Clear(bool clearAll = true);

    /// <summary>跳转到指定位置</summary>
    Task MoveToIndexAsync(int index);

    /// <summary>跳转到指定 Provider 曲目</summary>
    Task MoveToAsync(ProvidableItemBase item);

    /// <summary>下一首</summary>
    Task MoveNextAsync(bool userInitiated = false);

    /// <summary>上一首</summary>
    Task MovePreviousAsync();

    /// <summary>
    /// 切换播放策略
    /// </summary>
    /// <param name="strategyId">三字母策略 Id (seq/sgl/shf/shn/pfm/ltg)</param>
    /// <param name="persist">是否保存为普通播放模式偏好。</param>
    void SetStrategy(string strategyId, bool persist = true);

    /// <summary>
    /// 切换过渡策略
    /// </summary>
    /// <param name="transitionId">三字母过渡 Id (dir/xfd/gap)</param>
    void SetTransition(string transitionId);

    /// <summary>
    /// 曲目自然播放结束时由 PlaybackControlService 调用
    /// </summary>
    Task OnTrackEndedAsync();

    /// <summary>
    /// 播放位置更新时由 PlaybackControlService 调用（用于过渡策略的预加载等）
    /// </summary>
    void OnPositionTick(TimeSpan position, TimeSpan duration);

    /// <summary>
    /// 通知列表追加完成（触发 PlaylistChanged 消息）
    /// </summary>
    void NotifyAppendDone();

    /// <summary>
    /// 根据来源 ID 追加歌曲（pl=歌单, ns=单曲, al=专辑, sh/sa=歌手, rd=电台）
    /// </summary>
    Task<bool> AppendNcSourceAsync(string sourceId);

    /// <summary>
    /// 根据来源 Kind + Id 直接追加歌曲（替代 string-based 路由）
    /// </summary>
    Task<bool> AppendSourceByKindAsync(SongListQueueScopeKind kind, string id);

    /// <summary>追加歌单</summary>
    Task<bool> AppendPlayListAsync(string playlistId);

    /// <summary>追加电台节目</summary>
    Task<bool> AppendRadioListAsync(string radioId, bool asc = false);

    // ────────────── Shuffle / 本地文件 / 通知 ──────────────

    /// <summary>随机播放索引列表</summary>
    List<int> ShuffleList { get; }

    /// <summary>当前随机播放位置</summary>
    int ShufflingIndex { get; set; }

    /// <summary>生成随机播放列表</summary>
    void CreateShufflePlayLists();

    /// <summary>恢复当前播放索引而不触发播放</summary>
    void RestoreNowPlayingIndex(int index);

    /// <summary>反转播放列表</summary>
    void ReverseList();
}
