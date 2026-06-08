using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListController;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.LocalProvider;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

/// <summary>
/// UI-facing playlist facade backed by PlayCore.
/// </summary>
public sealed partial class PlaylistService : IPlaylistService, IDisposable
{
    public event EventHandler<PlaylistChangedEventArgs>? PlaylistChanged;

    private readonly PlaybackStateService _state;
    private readonly IPlaybackControlService _control;
    private readonly INotificationService _notification;
    private readonly Setting _setting;
    private readonly IReadOnlyDictionary<SongListQueueScopeKind, IQueueSourceProvider> _providersByKind;
    private readonly IReadOnlyDictionary<string, IQueueSourceProvider> _providersByPrefix;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly PlayCoreBase _playCore;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _trackEndLock = new(1, 1);

    private string _activeStrategyId;
    private string _activeTransitionId;
    private string _playSourceId = string.Empty;
    private bool _disposed;

    public PlaylistService(
        PlaybackStateService state,
        IPlaybackControlService control,
        INotificationService notification,
        Setting setting,
        IEnumerable<IQueueSourceProvider> queueSourceProviders,
        IBackgroundTaskRunner taskRunner,
        PlayCoreBase playCore)
    {
        _state = state;
        _control = control;
        _notification = notification;
        _setting = setting;
        _taskRunner = taskRunner;
        _playCore = playCore;

        var providerList = queueSourceProviders.ToList();
        _providersByKind = providerList.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.First());
        var byPrefix = providerList.ToDictionary(p => p.Prefix, StringComparer.Ordinal);
        if (byPrefix.TryGetValue(QueueSourcePrefixes.Singer, out var singerProvider))
            byPrefix[QueueSourcePrefixes.SingerAlias] = singerProvider;
        _providersByPrefix = byPrefix;

        _activeStrategyId = string.IsNullOrWhiteSpace(_setting.ActiveStrategyId) ? "seq" : _setting.ActiveStrategyId;
        _activeTransitionId = _setting.CrossFade ? "xfd" : "dir";
        _state.ActiveStrategyId = _activeStrategyId;
        _state.ActiveTransitionId = _activeTransitionId;
    }

    public IReadOnlyList<PlaybackQueueItemSnapshot> QueueItemsSnapshot
        => ProviderQueueSnapshot.Select(CreateQueueItemSnapshot).ToArray();

    public IReadOnlyList<SingleSongBase> ProviderItems
        => ProviderQueueSnapshot.Where(item => item is not null).Select(item => item!).ToArray();

    public IReadOnlyList<SingleSongBase?> ProviderQueueSnapshot
        => GetPlayCorePlaylist().Select<SingleSongBase, SingleSongBase?>(item => item).ToArray();

    public int QueueCount => ProviderQueueSnapshot.Count;

    public int NowPlayingIndex => _state.NowPlayingIndex;

    public SingleSongBase? NowPlayingProviderItem => _state.NowPlayingProviderItem;

    public string ActiveStrategyId => _activeStrategyId;

    public string ActiveTransitionId => _activeTransitionId;

    public bool IsInFm => _state.IsInFm;

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

    public void AppendLocalItem(LocalSong item, int position = -1) => AppendItem(item, position);

    public void AppendItem(ProvidableItemBase item, int position = -1)
    {
        if (item is not SingleSongBase song)
            return;

        RunSynchronously(InsertSongAsync(song, position));
    }

    public void SetItemInfoTag(ProvidableItemBase item, string infoTag)
    {
        // Provider-backed queue rows do not currently model InfoTag.
    }

    public void AppendLocalItems(IEnumerable<LocalSong> items, bool clearFirst = false)
        => AppendItems(items.Cast<SingleSongBase>(), clearFirst);

    public void AppendItems(IEnumerable<ProvidableItemBase> items, bool clearFirst = false)
        => AppendItems(items.OfType<SingleSongBase>(), clearFirst);

    public void AppendItems(IEnumerable<SingleSongBase> items, bool clearFirst = false)
    {
        var songs = items.ToList();
        if (songs.Count == 0)
            return;

        if (clearFirst)
            ExitPersonalFmForSourceChange();

        RunSynchronously(AppendSongsAsync(songs, clearFirst));
    }

    public List<int> AppendItems(IEnumerable<SingleSongBase> items, int position)
    {
        var songs = items.ToList();
        if (songs.Count == 0)
            return [];

        var queueCount = QueueCount;
        var insertAt = position < 0 ? queueCount : Math.Min(position, queueCount);
        RunSynchronously(AppendSongsAsync(songs, clearFirst: false, insertAt));
        return Enumerable.Range(insertAt, songs.Count).ToList();
    }

    public void RemoveAt(int index)
    {
        var snapshot = ProviderQueueSnapshot;
        if (index < 0 || index >= snapshot.Count || snapshot[index] is not { } song)
            return;

        RunSynchronously(RemoveAtAsync(index, song));
    }

    public void Clear(bool clearAll = true)
    {
        ExitPersonalFmForSourceChange();
        RunSynchronously(ClearAsync(clearAll));
    }

    private static PlaybackQueueItemSnapshot CreateQueueItemSnapshot(SingleSongBase? providerItem, int index)
    {
        if (providerItem is not null)
        {
            return new PlaybackQueueItemSnapshot(
                index,
                providerItem.Name ?? string.Empty,
                providerItem is IHasTranslation translatedProvider ? translatedProvider.Translation ?? string.Empty : string.Empty,
                providerItem.CreatorList is { Count: > 0 } creators ? string.Join("; ", creators) : string.Empty,
                providerItem);
        }

        return new PlaybackQueueItemSnapshot(index, string.Empty, string.Empty, string.Empty, null);
    }

    private async Task InsertSongAsync(SingleSongBase song, int position)
    {
        await _playCore.InsertSongAsync(song, position).ConfigureAwait(false);
        await RefreshAfterPlaylistChangedAsync().ConfigureAwait(false);
    }

    private static void RunSynchronously(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private async Task AppendSongsAsync(List<SingleSongBase> songs, bool clearFirst, int insertAt = -1)
    {
        if (clearFirst)
        {
            await _playCore.StopAsync().ConfigureAwait(false);
            await _playCore.RemoveAllSongAsync().ConfigureAwait(false);
            _state.ClearNowPlaying();
        }

        await _playCore.InsertSongRangeAsync(songs, insertAt).ConfigureAwait(false);
        await RefreshAfterPlaylistChangedAsync().ConfigureAwait(false);
    }

    private async Task RemoveAtAsync(int index, SingleSongBase song)
    {
        await _playCore.RemoveSongAsync(song).ConfigureAwait(false);

        if (index == _state.NowPlayingIndex)
        {
            var nextIndex = Math.Min(index, Math.Max(QueueCount - 1, -1));
            if (nextIndex >= 0)
                await MoveToIndexAsync(nextIndex).ConfigureAwait(false);
            else
                _state.ClearNowPlaying();
        }
        else if (index < _state.NowPlayingIndex)
        {
            _state.NowPlayingIndex--;
        }

        await RefreshAfterPlaylistChangedAsync().ConfigureAwait(false);
    }

    private async Task ClearAsync(bool clearAll)
    {
        if (clearAll)
        {
            await _playCore.StopAsync().ConfigureAwait(false);
            await _playCore.RemoveAllSongAsync().ConfigureAwait(false);
            _state.ClearNowPlaying();
        }
        else if (NowPlayingProviderItem is { } current)
        {
            await _playCore.RemoveAllSongAsync().ConfigureAwait(false);
            await _playCore.InsertSongAsync(current).ConfigureAwait(false);
            await MoveToIndexAsync(0).ConfigureAwait(false);
        }

        await RefreshAfterPlaylistChangedAsync().ConfigureAwait(false);
    }

    private List<SingleSongBase> GetPlayCorePlaylist()
    {
        try
        {
            return _playCore.CurrentPlayList?.GetPlayListAsync().GetAwaiter().GetResult() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task RefreshAfterPlaylistChangedAsync(bool isShuffleTrigger = false)
    {
        await SyncIndexFromPlayCoreAsync().ConfigureAwait(false);
        SendPlaylistChanged(isShuffleTrigger);
    }

    private async Task SyncIndexFromPlayCoreAsync()
    {
        if (_playCore.CurrentPlayListController is IIndexedPlayListController indexed)
            _state.NowPlayingIndex = await indexed.GetCurrentIndexAsync().ConfigureAwait(false);

        _state.SetNowPlaying(_playCore.CurrentSong);
        ShufflingIndex = ShuffleList.IndexOf(_state.NowPlayingIndex);
    }

    private void SendPlaylistChanged(bool isShuffleTrigger = false)
    {
        _taskRunner.Forget(_notification.InvokeOnUIThread(() =>
            PlaylistChanged?.Invoke(this, new PlaylistChangedEventArgs(isShuffleTrigger))),
            "publish playlist changed");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _trackEndLock.Dispose();
    }
}
