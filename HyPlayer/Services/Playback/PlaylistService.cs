using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.Storage;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 播放列表服务 — 编排播放策略 (<see cref="IPlayStrategy"/>) 与过渡策略 (<see cref="ITrackTransition"/>)，
/// 管理播放列表内容和播放顺序。
/// </summary>
public sealed partial class PlaylistService : IPlaylistService, IDisposable
{
    private readonly Dictionary<string, IPlayStrategy> _strategies;
    private readonly Dictionary<string, ITrackTransition> _transitions;
    private readonly PlaybackStateService _state;
    private readonly IPlaybackControlService _control;
    private readonly IPlayer _player;
    private readonly INotificationService _notification;
    private readonly Setting _setting;
    private readonly ILocalFileImportService _localFileImport;
    private readonly INeteaseQueueSourceService _neteaseQueueSource;
    private readonly IBackgroundTaskRunner _taskRunner;

    private readonly List<HyPlayItem> _items = new();
    private readonly Lock _lock = new();

    private IPlayStrategy _activeStrategy;
    private ITrackTransition _activeTransition;
    private int _nowPlayingIndex = -1;
    private CancellationTokenSource? _trackEndCts;
    private readonly SemaphoreSlim _trackEndLock = new(1, 1);

    /// <summary>
    /// 初始化 <see cref="PlaylistService"/>。
    /// </summary>
    /// <param name="strategies">所有已注册的播放策略</param>
    /// <param name="transitions">所有已注册的过渡策略</param>
    /// <param name="state">播放状态中心</param>
    /// <param name="control">播放控制服务</param>
    /// <param name="player">底层播放器</param>
    /// <param name="api">网易云 API 处理器</param>
    public PlaylistService(
        IEnumerable<IPlayStrategy> strategies,
        IEnumerable<ITrackTransition> transitions,
        PlaybackStateService state,
        IPlaybackControlService control,
        IPlayer player,
        INotificationService notification,
        Setting setting,
        ILocalFileImportService localFileImport,
        INeteaseQueueSourceService neteaseQueueSource,
        IBackgroundTaskRunner taskRunner)
    {
        _strategies = strategies.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _transitions = transitions.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _state = state;
        _control = control;
        _player = player;
        _notification = notification;
        _setting = setting;
        _localFileImport = localFileImport;
        _neteaseQueueSource = neteaseQueueSource;
        _taskRunner = taskRunner;

        var strategyId = _setting.ActiveStrategyId;
        _activeStrategy = _strategies.GetValueOrDefault(strategyId)
                          ?? _strategies.GetValueOrDefault("seq")
                          ?? _strategies.Values.First();
        var transitionId = _setting.CrossFade ? "xfd" : "dir";
        _activeTransition = _transitions.GetValueOrDefault(transitionId)
                            ?? _transitions.GetValueOrDefault("dir")
                            ?? _transitions.Values.First();

        _state.ActiveStrategyId = _activeStrategy.Id;
        _setting.ActiveStrategyId = _activeStrategy.Id;
        _state.ActiveTransitionId = _activeTransition.Id;
    }

    /// <inheritdoc />
    public IReadOnlyList<HyPlayItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public int NowPlayingIndex => _nowPlayingIndex;

    /// <inheritdoc />
    public HyPlayItem? NowPlayingItem
    {
        get
        {
            lock (_lock)
            {
                return _nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count
                    ? _items[_nowPlayingIndex]
                    : null;
            }
        }
    }

    /// <inheritdoc />
    public string ActiveStrategyId => _activeStrategy.Id;

    /// <inheritdoc />
    public string ActiveTransitionId => _activeTransition.Id;

    /// <inheritdoc />
    public bool IsInFm => _state.IsInFm;

    /// <inheritdoc />
    public string PlaySourceId { get; set; } = string.Empty;

    // ────────────── 列表操作 ──────────────

    /// <inheritdoc />
    public void AppendItem(HyPlayItem item, int position = -1)
    {
        lock (_lock)
        {
            if (position < 0 || position >= _items.Count)
                _items.Add(item);
            else
                _items.Insert(position, item);
        }
    }

    /// <inheritdoc />
    public void AppendItems(IEnumerable<HyPlayItem> items, bool clearFirst = false)
    {
        lock (_lock)
        {
            if (clearFirst)
            {
                DisposePlayItems(_items);
                _items.Clear();
            }

            _items.AddRange(items);
        }
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _items.Count)
                return;

            if (_items.Count == 1)
            {
                Clear();
                return;
            }

            DisposePlayItem(_items[index]);
            _items.RemoveAt(index);

            // 调整当前播放索引
            if (index < _nowPlayingIndex)
            {
                _nowPlayingIndex--;
                SyncIndex();
            }
            else if (index == _nowPlayingIndex)
            {
                // 当前曲目被移除，索引不变但指向了新曲目
                if (_nowPlayingIndex >= _items.Count)
                    _nowPlayingIndex = _items.Count - 1;
                SyncIndex();
            }

            NotifyAppendDone();
        }
    }

    /// <inheritdoc />
    public void Clear(bool stopPlayback = true)
    {
        lock (_lock)
        {
            if (_items.Count == 0)
                return;

            if (stopPlayback && _player.GlobalPlaybackStatus == PlaybackStatus.Playing)
                _control.Pause();   

            _player.RemoveAllPlaybackSource();
            DisposePlayItems(_items);
            _items.Clear();
            _nowPlayingIndex = -1;
            SyncIndex();
            _state.NowPlayingItem = null;

            NotifyAppendDone();
        }
    }

    /// <inheritdoc />
    public void NotifyAppendDone()
    {
        _activeStrategy.OnPlaylistChanged(BuildStrategyContext());
        if (ActiveStrategyId == "shn")
            CreateShufflePlayLists();
        else
            SendPlaylistChanged();
    }

    // ────────────── 导航 ──────────────

    /// <inheritdoc />
    public async Task MoveNextAsync(bool userInitiated = false)
    {
        if (_items.Count == 0)
            return;

        if (userInitiated)
            await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
        if (nextIndex is null)
            return;

        HyPlayItem item;
        lock (_lock)
        {
            _nowPlayingIndex = nextIndex.Value;
            SyncIndex();
            item = _items[_nowPlayingIndex];
        }

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    /// <inheritdoc />
    public async Task MovePreviousAsync()
    {
        if (_items.Count == 0)
            return;

        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var prevIndex = _activeStrategy.GetPrevious(BuildStrategyContext());
        if (prevIndex is null)
            return;

        HyPlayItem item;
        lock (_lock)
        {
            _nowPlayingIndex = prevIndex.Value;
            SyncIndex();
            item = _items[_nowPlayingIndex];
        }

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    /// <inheritdoc />
    public async Task MoveToAsync(HyPlayItem item)
    {
        int index;
        lock (_lock)
        {
            index = _items.IndexOf(item);
        }
        if (index < 0)
            return;

        // 中断正在进行的过渡
        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        lock (_lock)
        {
            _nowPlayingIndex = index;
            SyncIndex();
        }

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    // ────────────── 曲目结束处理 ──────────────

    /// <inheritdoc />
    public async Task OnTrackEndedAsync()
    {
        if (!await _trackEndLock.WaitAsync(0)) return;

        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = new CancellationTokenSource();
        var ct = _trackEndCts.Token;

        try
        {
            var action = _activeStrategy.OnTrackEnded(BuildStrategyContext());

            switch (action)
            {
                case PlayStrategyAction.MoveNext:
                    if (ShouldReplaySingleItem())
                    {
                        await _control.SeekAsync(TimeSpan.Zero);
                        _control.Play();
                        break;
                    }

                    await _activeTransition.OnTrackEndedAsync(BuildTransitionContext());
                    break;

                case PlayStrategyAction.Replay:
                    await _control.SeekAsync(TimeSpan.Zero);
                    break;

                case PlayStrategyAction.LoadMore:
                    if (_activeStrategy is IAsyncPlayStrategy asyncStrategy)
                    {
                        var moreItems = await asyncStrategy.LoadMoreAsync(
                            BuildStrategyContext(), ct);
                        lock (_lock)
                        {
                            _items.AddRange(moreItems);
                        }
                        NotifyAppendDone();
                        await _activeTransition.OnTrackEndedAsync(BuildTransitionContext());
                    }
                    break;

                case PlayStrategyAction.Stop:
                    // 服务器驱动模式，不做任何操作
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 新的播放结束处理已接管。
        }
        finally
        {
            _trackEndLock.Release();
        }
    }

    /// <inheritdoc />
    public void OnPositionTick(TimeSpan position, TimeSpan duration)
    {
        if (!ShouldRunTransitionOnPositionTick())
            return;

        _activeTransition.OnPositionTick(BuildTransitionContext(position, duration));
    }

    private bool ShouldRunTransitionOnPositionTick()
    {
        var action = _activeStrategy.OnTrackEnded(BuildStrategyContext());
        return action is PlayStrategyAction.MoveNext or PlayStrategyAction.LoadMore;
    }

    // ────────────── 策略切换 ──────────────

    /// <inheritdoc />
    public void SetStrategy(string strategyId)
    {
        if (!_strategies.TryGetValue(strategyId, out var strategy))
            return;

        _activeStrategy = strategy;
        _state.ActiveStrategyId = strategyId;
        _setting.ActiveStrategyId = strategyId;
        _activeStrategy.OnPlaylistChanged(BuildStrategyContext());

        if (strategyId == "shn")
            CreateShufflePlayLists();
        else
            SendPlaylistChanged();
    }

    /// <inheritdoc />
    public void SetTransition(string transitionId)
    {
        if (!_transitions.TryGetValue(transitionId, out var transition))
            return;

        _activeTransition.Reset();
        _activeTransition = transition;
        _state.ActiveTransitionId = transitionId;
    }

    // ────────────── 本地文件追加（占位） ──────────────

    /// <inheritdoc />
    public Task AppendStorageFilesAsync(IEnumerable<StorageFile> files)
    {
        // 本地文件加载逻辑由 MediaProvider 层处理，此处仅作接口占位
        return Task.CompletedTask;
    }

    // ────────────── NCSong 相关 ──────────────

    /// <inheritdoc />
    public HyPlayItem NCSongToPlayItem(NCSong ncSong)
    {
        return new HyPlayItem
        {
            ItemType = ncSong.Type,
            InfoTag = ncSong.Alias,
            Album = ncSong.Album,
            Artist = ncSong.Artist,
            Id = ncSong.SongId,
            Translation = ncSong.TranslatedName,
            Name = ncSong.SongName,
            TrackId = ncSong.TrackId,
            CDName = ncSong.CDName,
            LengthInMilliseconds = ncSong.LengthInMilliseconds
        };
    }

    /// <inheritdoc />
    public HyPlayItem AppendNcSong(NCSong ncSong, int position = -1)
    {
        var hpi = NCSongToPlayItem(ncSong);
        lock (_lock)
        {
            if (_items.Contains(hpi))
                return hpi;

            if (position < 0 || position >= _items.Count)
                _items.Add(hpi);
            else
                _items.Insert(position, hpi);
        }

        NotifyAppendDone();
        return hpi;
    }

    /// <inheritdoc />
    public void AppendNcSongs(IList<NCSong> ncSongs, bool clearFirst = true, string currentSongId = "-1")
    {
        if (ncSongs == null) return;
        try
        {
            if (clearFirst)
            {
                lock (_lock)
                {
                    DisposePlayItems(_items);
                    _items.Clear();
                }
            }

            foreach (var ncSong in ncSongs)
            {
                var hpi = NCSongToPlayItem(ncSong);
                lock (_lock) { _items.Add(hpi); }
            }

            NotifyAppendDone();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    /// <inheritdoc />
    public List<HyPlayItem> AppendNcSongRange(List<NCSong> ncSongs, int position = -1)
    {
        lock (_lock)
        {
            if (position < 0)
                position = _items.Count;

            var insertList = ncSongs.Select(NCSongToPlayItem)
                .Where(t => !_items.Contains(t))
                .ToList();

            if (insertList.Count <= 0)
                return insertList;

            _items.InsertRange(position, insertList);
            NotifyAppendDone();
            return insertList;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AppendNcSourceAsync(string sourceId)
    {
        var result = await _neteaseQueueSource.LoadSourceAsync(sourceId);
        AppendNcSongBatches(result);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> AppendPlayListAsync(string playlistId)
    {
        var result = await _neteaseQueueSource.LoadPlaylistAsync(playlistId);
        AppendNcSongBatches(result);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> AppendRadioListAsync(string radioId, bool asc = false)
    {
        var result = await _neteaseQueueSource.LoadRadioListAsync(radioId, asc);
        AppendNcSongBatches(result);
        return result.Success;
    }

    private async Task<bool> AppendSingerHotAsync(string id)
    {
        var result = await _neteaseQueueSource.LoadSingerHotAsync(id);
        AppendNcSongBatches(result);
        return result.Success;
    }

    private async Task<bool> AppendAlbumAsync(string albumId)
    {
        var result = await _neteaseQueueSource.LoadAlbumAsync(albumId);
        AppendNcSongBatches(result);
        return result.Success;
    }

    private void AppendNcSongBatches(NeteaseQueueSourceLoadResult result)
    {
        if (!result.Success)
            return;

        try
        {
            var hasChanges = false;
            lock (_lock)
            {
                foreach (var batch in result.Batches)
                {
                    if (batch is not { Count: > 0 })
                        continue;

                    foreach (var ncSong in batch)
                    {
                        if (result.Batches.Count == 1 && batch.Count == 1)
                        {
                            var singleItem = NCSongToPlayItem(ncSong);
                            if (_items.Contains(singleItem))
                                continue;

                            _items.Add(singleItem);
                        }
                        else
                        {
                            _items.Add(NCSongToPlayItem(ncSong));
                        }

                        hasChanges = true;
                    }
                }
            }

            if (hasChanges)
                NotifyAppendDone();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

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

    // ────────────── Shuffle / 本地文件 / 通知 ──────────────

    /// <inheritdoc />
    public List<int> ShuffleList { get; } = [];

    /// <inheritdoc />
    public int ShufflingIndex { get; set; } = -1;

    /// <inheritdoc />
    public StorageFile? NowPlayingStorageFile { get; private set; }

    /// <inheritdoc />
    public async Task PickLocalFileAsync()
    {
        var items = await _localFileImport.PickLocalFilesAsync();
        if (items.Count == 0) return;

        lock (_lock) { _items.AddRange(items); }

        NotifyAppendDone();
        HyPlayItem? lastItem;
        lock (_lock) { lastItem = _items.LastOrDefault(); }
        if (lastItem != null)
            await MoveToAsync(lastItem);
    }

    /// <inheritdoc />
    public async Task<HyPlayItem> LoadStorageFileAsync(StorageFile sf, bool nocheck163 = false)
    {
        return await _localFileImport.LoadStorageFileAsync(sf, nocheck163);
    }

    /// <inheritdoc />
    public void CreateShufflePlayLists()
    {
        ShuffleList.Clear();
        var currentSongId = NowPlayingItem?.Id ?? "-1";
        lock (_lock)
        {
            if (_items.Count != 0)
            {
                HashSet<int> shuffledNumbers = [];
                if (currentSongId != "-1")
                {
                    int playItemIndex = _items.FindIndex(s => s.ToNCSong().SongId == currentSongId);
                    if (playItemIndex != -1)
                    {
                        shuffledNumbers.Add(playItemIndex);
                        ShuffleList.Add(playItemIndex);
                    }
                }

                while (shuffledNumbers.Count < _items.Count)
                {
                    var indexShuffled = RandomNumberGenerator.GetInt32(_items.Count);
                    if (shuffledNumbers.Add(indexShuffled))
                        ShuffleList.Add(indexShuffled);
                }
            }
        }

        SendPlaylistChanged(true);
    }

    /// <inheritdoc />
    public void RestoreNowPlayingIndex(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _items.Count)
                return;

            _nowPlayingIndex = index;
            SyncIndex();
        }
    }

    /// <inheritdoc />
    public void ReverseList()
    {
        lock (_lock)
        {
            _items.Reverse();
            if (_nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count)
                _nowPlayingIndex = _items.Count - _nowPlayingIndex - 1;
            SyncIndex();
        }
    }

    /// <summary>
    /// Disposes the internal <see cref="CancellationTokenSource"/> used for track-end handling.
    /// </summary>
    public void Dispose()
    {
        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = null;
        DisposePlayItems(_items);
        _items.Clear();
        _state.NowPlayingItem = null;
        _trackEndLock.Dispose();
    }
}
