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
    private readonly IReadOnlyDictionary<SongListQueueScopeKind, IQueueSourceProvider> _providersByKind;
    private readonly IReadOnlyDictionary<string, IQueueSourceProvider> _providersByPrefix;
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
        IEnumerable<IQueueSourceProvider> queueSourceProviders,
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

        var providerList = queueSourceProviders.ToList();
        _providersByKind = providerList.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.First());
        var byPrefix = providerList.ToDictionary(p => p.Prefix, StringComparer.Ordinal);
        if (byPrefix.TryGetValue(QueueSourcePrefixes.Singer, out var singerProvider))
            byPrefix[QueueSourcePrefixes.SingerAlias] = singerProvider;
        _providersByPrefix = byPrefix;

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

    private string _playSourceId = string.Empty;

    /// <inheritdoc />
    public string PlaySourceId
    {
        get => _playSourceId;
        set
        {
            if (_playSourceId == value)
                return;

            ExitPersonalFmForSourceChange();
            _playSourceId = value;
        }
    }

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
        if (clearFirst)
            ExitPersonalFmForSourceChange();

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
            }
            else if (index == _nowPlayingIndex)
            {
                // 当前曲目被移除，索引不变但指向了新曲目
                if (_nowPlayingIndex >= _items.Count)
                    _nowPlayingIndex = _items.Count - 1;
            }

            NotifyAppendDone();
        }
    }

    /// <inheritdoc />
    public void Clear(bool stopPlayback = true)
    {
        ExitPersonalFmForSourceChange();

        lock (_lock)
        {
            if (_items.Count == 0)
                return;

            if (stopPlayback)
            {
                if (_player.GlobalPlaybackStatus == PlaybackStatus.Playing)
                    _control.Pause();

                _player.RemoveAllPlaybackSource();
                DisposePlayItems(_items);
                _items.Clear();
                _nowPlayingIndex = -1;
                _state.NowPlayingItem = null;
            }
            else
            {
                HyPlayItem? nowPlayingItem = null;
                if (_nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count)
                    nowPlayingItem = _items[_nowPlayingIndex];

                var itemsToDispose = _items.Where(t => t != nowPlayingItem).ToList();

                _items.Clear();
                DisposePlayItems(itemsToDispose);
            }

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
}
