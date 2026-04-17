using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HyPlayer.Classes;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 播放列表服务，管理播放列表内容和播放顺序。
/// 内部组合 <see cref="IPlayStrategy"/>（决定下一首）和 <see cref="ITrackTransition"/>（决定怎么过渡）。
/// </summary>
public interface IPlaylistService
{
    /// <summary>当前播放列表（只读视图）</summary>
    IReadOnlyList<HyPlayItem> Items { get; }

    /// <summary>当前播放索引</summary>
    int NowPlayingIndex { get; }

    /// <summary>当前播放曲目</summary>
    HyPlayItem? NowPlayingItem { get; }

    /// <summary>当前播放策略 Id</summary>
    string ActiveStrategyId { get; }

    /// <summary>当前过渡策略 Id</summary>
    string ActiveTransitionId { get; }

    /// <summary>是否处于私人 FM 模式</summary>
    bool IsInFm { get; }

    /// <summary>播放来源标识（用于历史记录等）</summary>
    string PlaySourceId { get; set; }

    /// <summary>追加单曲</summary>
    void AppendItem(HyPlayItem item, int position = -1);

    /// <summary>批量追加</summary>
    void AppendItems(IEnumerable<HyPlayItem> items, bool clearFirst = false);

    /// <summary>移除指定位置的曲目</summary>
    void RemoveAt(int index);

    /// <summary>清空播放列表</summary>
    void Clear(bool stopPlayback = true);

    /// <summary>跳转到指定曲目</summary>
    Task MoveToAsync(HyPlayItem item);

    /// <summary>下一首</summary>
    Task MoveNextAsync(bool userInitiated = false);

    /// <summary>上一首</summary>
    Task MovePreviousAsync();

    /// <summary>追加本地文件</summary>
    Task AppendStorageFilesAsync(IEnumerable<StorageFile> files);

    /// <summary>
    /// 切换播放策略
    /// </summary>
    /// <param name="strategyId">三字母策略 Id (seq/sgl/shf/shn/pfm/ltg)</param>
    void SetStrategy(string strategyId);

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
    void NotifyAppendDone(bool isShuffleTrigger = false);

    // ────────────── NCSong 相关 ──────────────

    /// <summary>将 NCSong 转换为 HyPlayItem</summary>
    HyPlayItem NCSongToPlayItem(NCSong ncSong);

    /// <summary>追加单首网易云歌曲</summary>
    HyPlayItem AppendNcSong(NCSong ncSong, int position = -1);

    /// <summary>批量追加网易云歌曲</summary>
    void AppendNcSongs(IList<NCSong> ncSongs, bool clearFirst = true, string currentSongId = "-1");

    /// <summary>批量追加并返回实际追加的列表</summary>
    List<HyPlayItem> AppendNcSongRange(List<NCSong> ncSongs, int position = -1);

    /// <summary>
    /// 根据来源 ID 追加歌曲（pl=歌单, ns=单曲, al=专辑, sh/sa=歌手, rd=电台）
    /// </summary>
    Task<bool> AppendNcSourceAsync(string sourceId);

    /// <summary>追加歌单</summary>
    Task<bool> AppendPlayListAsync(string playlistId);

    /// <summary>追加电台节目</summary>
    Task<bool> AppendRadioListAsync(string radioId, bool asc = false);

    // ────────────── Shuffle / 本地文件 / 通知 ──────────────

    /// <summary>随机播放索引列表</summary>
    List<int> ShuffleList { get; }

    /// <summary>当前随机播放位置</summary>
    int ShufflingIndex { get; set; }

    /// <summary>当前正在播放的本地 StorageFile（可为 null）</summary>
    StorageFile? NowPlayingStorageFile { get; }

    /// <summary>弹出文件选择器并追加本地文件</summary>
    Task PickLocalFileAsync();

    /// <summary>加载单个 StorageFile 为 HyPlayItem</summary>
    Task<HyPlayItem> LoadStorageFileAsync(StorageFile file, bool nocheck163 = false);

    /// <summary>生成随机播放列表</summary>
    Task CreateShufflePlayLists(string currentSongId = "-1");

    /// <summary>反转播放列表</summary>
    void ReverseList();

    /// <summary>通知当前播放曲目变更（发送 TrackChangedMessage）</summary>
    void NotifyPlayItemChanged(HyPlayItem item);
}
