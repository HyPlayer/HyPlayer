using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── 内部辅助 ──────────────

    private void InsertQueueItem(ProvidableItemBase item, int position = -1)
    {
        InsertQueueItem(item as SingleSongBase, position);
    }

    private void InsertQueueItem(SingleSongBase? providerItem, int position = -1)
    {
        if (position < 0 || position >= _providerItems.Count)
        {
            _providerItems.Add(providerItem);
        }
        else
        {
            _providerItems.Insert(position, providerItem);
        }
    }

    private void InsertQueueItems(IEnumerable<ProvidableItemBase> items)
    {
        foreach (var item in items)
            InsertQueueItem(item);
    }

    private void InsertQueueItems(IEnumerable<SingleSongBase> items)
    {
        foreach (var item in items)
            InsertQueueItem(item);
    }

    /// <summary>
    /// 构建播放策略上下文
    /// </summary>
    private PlayStrategyContext BuildStrategyContext()
    {
        lock (_lock)
        {
            var providerQueueItems = _providerItems.ToArray();
            var providerItems = providerQueueItems.OfType<SingleSongBase>().ToArray();
            return new PlayStrategyContext
            {
                CurrentIndex = _nowPlayingIndex,
                QueueCount = QueueCount,
                ProviderItems = providerItems,
                ProviderQueueItems = providerQueueItems,
                CurrentProviderItem = NowPlayingProviderItem,
                UpdateShuffleActions = CreateShufflePlayLists,
                ShuffledIndex = ActiveStrategyId == "shn" ? ShufflingIndex : null,
                ShuffledItems = ActiveStrategyId == "shn" ? ShuffleList : null
            };
        }
    }

    /// <summary>
    /// 构建过渡策略上下文，将回调绑定到本服务
    /// </summary>
    private TrackTransitionContext BuildTransitionContext() => BuildTransitionContext(_state.Position, _state.Duration);

    private TrackTransitionContext BuildTransitionContext(TimeSpan position, TimeSpan duration) => new()
    {
        Position = position,
        Duration = duration,
        CurrentProviderItem = NowPlayingProviderItem,
        RequestNextProviderItemAsync = RequestNextProviderItemAsync,
        CommitProviderItemAsync = CommitProviderItemAsync,
        LoadProviderMediaSourceAsync = _control.LoadAndPlayAsync,
        PreloadProviderPlaybackSourceAsync = song => _control.PreloadTransitionPlaybackSourceAsync(song),
        Player = _player,
        TaskRunner = _taskRunner
    };

    /// <summary>
    /// 供过渡策略回调：获取下一首 Provider 曲目并保持和旧队列索引同步。
    /// </summary>
    private Task<SingleSongBase?> RequestNextProviderItemAsync(bool advance)
    {
        var nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
        if (nextIndex is null)
            return Task.FromResult<SingleSongBase?>(null);

        SingleSongBase? item;
        lock (_lock)
        {
            if (!advance && nextIndex.Value == _nowPlayingIndex)
                return Task.FromResult<SingleSongBase?>(null);

            item = nextIndex.Value >= 0 && nextIndex.Value < _providerItems.Count
                ? _providerItems[nextIndex.Value]
                : null;
            if (advance)
            {
                _nowPlayingIndex = nextIndex.Value;
                SyncIndex();
            }
        }

        return Task.FromResult(item);
    }

    private bool ShouldReplaySingleItem()
    {
        lock (_lock)
        {
            return _providerItems.Count == 1 && _nowPlayingIndex == 0;
        }
    }

    private Task CommitProviderItemAsync(SingleSongBase item)
    {
        lock (_lock)
        {
            var index = _providerItems.FindIndex(providerItem => providerItem is not null
                && providerItem.ProviderId == item.ProviderId
                && providerItem.TypeId == item.TypeId
                && providerItem.ActualId == item.ActualId);
            if (index >= 0)
            {
                _nowPlayingIndex = index;
                SyncIndex();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 同步内部索引到 <see cref="PlaybackStateService"/>
    /// </summary>
    private void SyncIndex()
    {
        _state.NowPlayingIndex = _nowPlayingIndex;
        _state.SetNowPlaying(NowPlayingItem, NowPlayingProviderItem);
        ShufflingIndex = ShuffleList.IndexOf(_nowPlayingIndex);
    }

    /// <summary>
    /// 发送播放列表变更消息
    /// </summary>
    private void SendPlaylistChanged(bool isShuffleTrigger = false)
    {
        SyncIndex();
        _taskRunner.Forget(_notification.InvokeOnUIThread(() =>
            PlaylistChanged?.Invoke(this, new PlaylistChangedEventArgs(isShuffleTrigger))),
            "publish playlist changed");
    }

}
