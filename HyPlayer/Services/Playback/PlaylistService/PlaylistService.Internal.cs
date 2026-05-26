using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.LocalProvider;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── 内部辅助 ──────────────

    private void InsertQueueItem(HyPlayItem item, int position = -1)
    {
        InsertQueueItem(item, null, position);
    }

    private void InsertQueueItem(ProvidableItemBase item, int position = -1)
    {
        var playItem = FindLegacyQueueItem(item) ?? (item is LocalSong localSong ? ToLegacyQueueItem(localSong) : HyPlayItem.FromProviderItem(item));
        InsertQueueItem(playItem, item as SingleSongBase, position);
    }

    private static HyPlayItem ToLegacyQueueItem(LocalSong song)
    {
        return new HyPlayItem
        {
            ItemType = song.IsNcm ? HyPlayItemType.Netease : HyPlayItemType.Local,
            Album = new NCAlbum
            {
                AlbumType = HyPlayItemType.Local,
                Id = song.Album?.ActualId ?? string.Empty,
                Name = song.Album?.Name ?? string.Empty,
                Cover = string.Empty
            },
            Artist = song.Artists?.Select(artist => new NCArtist
            {
                Id = artist.ActualId ?? string.Empty,
                Name = artist.Name ?? string.Empty,
                Type = HyPlayItemType.Local
            }).ToList() ?? [],
            Bitrate = song.Bitrate,
            CDName = song.CdName ?? "01",
            Translation = song.Translation ?? string.Empty,
            LocalStorageFile = song.StorageFile,
            LocalFileTag = song.FileTag,
            Id = song.LegacyNeteaseId,
            IsLocalFile = true,
            LengthInMilliseconds = song.Duration,
            Name = song.Name ?? string.Empty,
            Size = 0,
            SubExt = song.ExtensionName ?? string.Empty,
            QualityTag = string.Empty,
            InfoTag = song.InfoTag ?? string.Empty,
            TrackId = song.TrackNumber,
            Url = song.ActualId ?? string.Empty,
            ProviderIdentityProviderId = song.ProviderId,
            ProviderIdentityTypeId = song.TypeId,
            ProviderIdentityActualId = song.ActualId ?? string.Empty
        };
    }

    private HyPlayItem? FindLegacyQueueItem(ProvidableItemBase item)
    {
        return _items.FirstOrDefault(existing =>
        {
            var identity = existing.GetItemIdentity();
            return identity.ProviderId == item.ProviderId
                   && identity.TypeId == item.TypeId
                   && identity.ActualId == item.ActualId;
        });
    }

    private void InsertQueueItem(HyPlayItem item, SingleSongBase? providerItem, int position = -1)
    {
        if (position < 0 || position >= _items.Count)
        {
            _items.Add(item);
            _providerItems.Add(providerItem);
        }
        else
        {
            _items.Insert(position, item);
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
        CurrentItem = NowPlayingItem,
        CurrentProviderItem = NowPlayingProviderItem,
        RequestNextItemAsync = RequestNextItemAsync,
        RequestNextProviderItemAsync = RequestNextProviderItemAsync,
        CommitItemAsync = CommitItemAsync,
        CommitProviderItemAsync = CommitProviderItemAsync,
        LoadMediaSourceAsync = LoadLegacyMediaSourceAsync,
        LoadProviderMediaSourceAsync = _control.LoadAndPlayAsync,
        PreloadProviderPlaybackSourceAsync = song => _control.PreloadTransitionPlaybackSourceAsync(song),
        Player = _player,
        TaskRunner = _taskRunner
    };

    private Task LoadLegacyMediaSourceAsync(HyPlayItem item, bool setAsPrimary, bool autoPlay, bool removeCurrentSongs)
    {
        SingleSongBase? providerItem;
        lock (_lock)
        {
            var index = _items.IndexOf(item);
            providerItem = index >= 0 && index < _providerItems.Count
                ? _providerItems[index]
                : null;
        }

        return providerItem is not null
            ? _control.LoadAndPlayAsync(providerItem, autoPlay, removeCurrentSongs)
            : Task.CompletedTask;
    }

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
