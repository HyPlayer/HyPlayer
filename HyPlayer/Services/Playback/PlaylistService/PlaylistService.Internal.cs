using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.Storage;

namespace HyPlayer.Services.Playback;

public sealed partial class PlaylistService
{
    // ────────────── 内部辅助 ──────────────

    /// <summary>
    /// 构建播放策略上下文
    /// </summary>
    private PlayStrategyContext BuildStrategyContext()
    {
        lock (_lock)
        {
            return new PlayStrategyContext
            {
                Items = _items.ToArray(),
                CurrentIndex = _nowPlayingIndex,
                CurrentItem = NowPlayingItem,
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
        CurrentItem = NowPlayingItem,
        RequestNextItemAsync = RequestNextItemAsync,
        CommitItemAsync = CommitItemAsync,
        LoadMediaSourceAsync = _control.LoadAndPlayAsync,
        Player = _player,
        TaskRunner = _taskRunner
    };

    /// <summary>
    /// 供过渡策略回调：获取下一首曲目但不改变播放索引
    /// </summary>
    private Task<HyPlayItem?> RequestNextItemAsync(bool advance)
    {
        var nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
        if (nextIndex is null)
            return Task.FromResult<HyPlayItem?>(null);

        HyPlayItem item;
        lock (_lock)
        {
            if (!advance && nextIndex.Value == _nowPlayingIndex)
                return Task.FromResult<HyPlayItem?>(null);

            item = _items[nextIndex.Value];
            if (advance)
            {
                _nowPlayingIndex = nextIndex.Value;
                SyncIndex();
            }
        }

        return Task.FromResult<HyPlayItem?>(item);
    }

    private bool ShouldReplaySingleItem()
    {
        lock (_lock)
        {
            return _items.Count == 1 && _nowPlayingIndex == 0;
        }
    }

    /// <summary>
    /// 供过渡策略回调：将预加载曲目提交为当前播放曲目。
    /// </summary>
    private Task CommitItemAsync(HyPlayItem item)
    {
        lock (_lock)
        {
            var index = _items.IndexOf(item);
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
        _state.NowPlayingItem = NowPlayingItem;
        ShufflingIndex = ShuffleList.IndexOf(_nowPlayingIndex);
    }

    /// <summary>
    /// 发送播放列表变更消息
    /// </summary>
    private void SendPlaylistChanged(bool isShuffleTrigger = false)
    {
        SyncIndex();
        _taskRunner.Forget(_notification.InvokeOnUIThread(() =>
            WeakReferenceMessenger.Default.Send(new PlaylistChangedMessage(isShuffleTrigger))),
            "publish playlist changed");
    }

    private static void DisposePlayItems(IEnumerable<HyPlayItem> items)
    {
        foreach (var item in items)
            DisposePlayItem(item);
    }

    private static void DisposePlayItem(HyPlayItem? item)
    {
        item?.PlayItem?.Dispose();
        if (item is not null)
            item.PlayItem = null;
    }
}
