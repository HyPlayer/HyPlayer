using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.History.Services;
using HyPlayer.Platform.Serialization;
using HyPlayer.Platform.Storage;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using LocalPlaybackProvider = HyPlayer.Platform.Playback.LocalProvider.LocalProvider;

namespace HyPlayer.Features.Playback.Services;

public sealed class PlaybackMemoryService : IPlaybackMemoryService, IDisposable
{
    private const int StateVersion = 1;
    private const string MemorySettingsKey = "playbackMemoryState";
    private const string MemoryFileName = "playbackMemoryState";
    private const int SaveDebounceMilliseconds = 1500;
    private const int SavePositionStepMilliseconds = 5000;
    private const int EndPositionResetThresholdMilliseconds = 5000;
    private readonly IPlaybackControlService _control;
    private readonly IHistoryService _history;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly IProvidableItemRangeProvidable _itemRangeProvider;
    private readonly ILocalFileImportService _localFileImport;
    private readonly INotificationService _notification;

    private readonly PlayCoreBase _playCore;
    private readonly IPlaybackQueueLoader _queueLoader;
    private readonly PlaybackSettings _playbackSettings;
    private readonly LocalLibrarySettings _localLibrarySettings;
    private readonly PlaybackStateService _state;
    private bool _initialized;
    private int _lastSavedPositionBucket = -1;
    private bool _restoring;
    private CancellationTokenSource? _saveDebounceCts;

    public PlaybackMemoryService(
        PlayCoreBase playCore,
        PlaybackStateService state,
        PlaybackSettings playbackSettings,
        LocalLibrarySettings localLibrarySettings,
        IPlaybackQueueLoader queueLoader,
        IPlaybackControlService control,
        IProvidableItemRangeProvidable itemRangeProvider,
        ILocalFileImportService localFileImport,
        IHistoryService history,
        INotificationService notification)
    {
        _playCore = playCore;
        _state = state;
        _playbackSettings = playbackSettings;
        _localLibrarySettings = localLibrarySettings;
        _queueLoader = queueLoader;
        _control = control;
        _itemRangeProvider = itemRangeProvider;
        _localFileImport = localFileImport;
        _history = history;
        _notification = notification;
    }

    public void Dispose()
    {
        _state.PropertyChanged -= State_PropertyChanged;
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _ioLock.Dispose();
    }

    public Task InitializeAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        _initialized = true;
        _state.PropertyChanged += State_PropertyChanged;
        return Task.CompletedTask;
    }

    public async Task RestoreAsync()
    {
        if (_restoring)
            return;

        _restoring = true;
        try
        {
            var memory = await ReadStateAsync().ConfigureAwait(false);
            if (memory is null)
                memory = await ReadLegacyStateAsync().ConfigureAwait(false);

            if (memory is null)
                return;

            await RestoreStateAsync(memory).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("恢复播放列表失败", ex.Message);
        }
        finally
        {
            _restoring = false;
        }
    }

    public async Task SaveNowAsync()
    {
        if (_restoring)
            return;

        _saveDebounceCts?.Cancel();
        await WriteCurrentStateAsync().ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        _saveDebounceCts?.Cancel();

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ApplicationData.Current.LocalSettings.Values.Remove(MemorySettingsKey);
            var file = await ApplicationData.Current.LocalCacheFolder.TryGetItemAsync(MemoryFileName);
            if (file is not null)
                await file.DeleteAsync();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task RestoreStateAsync(PlaybackMemoryState memory)
    {
        var restoredFromSource = await TryRestoreQueueFromSourceAsync(memory).ConfigureAwait(false);
        if (!restoredFromSource)
            await RestoreQueueFromSnapshotAsync(memory.Queue).ConfigureAwait(false);

        var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
        if (queue.Count == 0)
            return;

        if (!string.IsNullOrWhiteSpace(memory.PlaySourceId))
            _playCore.PlaySourceId = memory.PlaySourceId;
        else if (TryParseSource(memory, out var sourceKind, out var sourceId))
            _playCore.PlaySourceId = BuildPlaySourceId(sourceKind, sourceId) ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(memory.ActiveStrategyId))
        {
            await _control.SetPlayModeAsync(memory.ActiveStrategyId).ConfigureAwait(false);
            _playbackSettings.ActiveStrategyId = memory.ActiveStrategyId;
        }

        var playbackQueue = await _playCore.GetOrderedPlaylistAsync().ConfigureAwait(false);
        if (playbackQueue.Count == 0)
            playbackQueue = queue;

        var index = FindQueueIndex(playbackQueue, memory.CurrentItem);
        if (index < 0 && memory.CurrentIndex >= 0 && memory.CurrentIndex < playbackQueue.Count)
            index = memory.CurrentIndex;
        if (index < 0)
            index = 0;

        await _playCore.MovePointerToIndexAsync(index).ConfigureAwait(false);
        if (_playCore.CurrentSong is not { } currentSong)
            currentSong = playbackQueue[index];

        await _control.LoadAndPlayAsync(currentSong, false, false).ConfigureAwait(false);
        await _playCore.MovePointerToIndexAsync(index).ConfigureAwait(false);

        var restorePosition = GetRestorePosition(memory.PositionMilliseconds, currentSong.Duration);
        if (restorePosition > TimeSpan.Zero)
            await _control.SeekAsync(restorePosition).ConfigureAwait(false);
    }

    private async Task<bool> TryRestoreQueueFromSourceAsync(PlaybackMemoryState memory)
    {
        if (!TryParseSource(memory, out var kind, out var id))
            return false;

        await _control.ClearQueueAsync().ConfigureAwait(false);
        var success = await _queueLoader.AppendSourceByKindAsync(kind, id).ConfigureAwait(false);
        if (!success)
            return false;

        var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
        return queue.Count > 0;
    }

    private async Task RestoreQueueFromSnapshotAsync(IReadOnlyList<PlaybackItemIdentity> queue)
    {
        await _control.ClearQueueAsync().ConfigureAwait(false);
        var songs = await LoadSnapshotSongsAsync(queue).ConfigureAwait(false);
        if (songs.Count > 0)
            await _playCore.InsertSongRangeAsync(songs).ConfigureAwait(false);
    }

    private async Task<List<SingleSongBase>> LoadSnapshotSongsAsync(IReadOnlyList<PlaybackItemIdentity> identities)
    {
        var remoteIds = new List<string>();

        foreach (var identity in identities)
            if (identity.ProviderId != LocalPlaybackProvider.ProviderIdValue
                && !string.IsNullOrWhiteSpace(identity.TypeId)
                && !string.IsNullOrWhiteSpace(identity.ActualId))
                remoteIds.Add(identity.TypeId + identity.ActualId);

        var remoteSongsByKey = new Dictionary<string, SingleSongBase>(StringComparer.Ordinal);
        if (remoteIds.Count > 0)
        {
            var items = await _itemRangeProvider.GetProvidableItemsRangeAsync(remoteIds).ConfigureAwait(false);
            remoteSongsByKey = items
                .OfType<SingleSongBase>()
                .GroupBy(song => CreateKey(song.ProviderId, song.TypeId, song.ActualId))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        var result = new List<SingleSongBase>();
        foreach (var identity in identities)
        {
            if (identity.ProviderId == LocalPlaybackProvider.ProviderIdValue)
            {
                var path = identity.LocalPath ?? identity.ActualId;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        var song = await _localFileImport.LoadStorageFileAsync(file).ConfigureAwait(false);
                        result.Add(song);
                    }
                    catch
                    {
                        // Local files can disappear or lose access between app sessions.
                    }

                continue;
            }

            var key = CreateKey(identity.ProviderId, identity.TypeId, identity.ActualId);
            if (remoteSongsByKey.TryGetValue(key, out var remoteSong))
                result.Add(remoteSong);
        }

        return result;
    }

    private async Task WriteCurrentStateAsync()
    {
        var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
        if (queue.Count == 0)
        {
            await ClearAsync().ConfigureAwait(false);
            return;
        }

        var currentItem = _state.NowPlayingProviderItem ?? _playCore.CurrentSong;
        var currentIndex = _state.NowPlayingIndex >= 0
            ? _state.NowPlayingIndex
            : await _playCore.GetCurrentIndexAsync().ConfigureAwait(false);
        var state = new PlaybackMemoryState(
            StateVersion,
            string.IsNullOrWhiteSpace(_playCore.PlaySourceId) ? null : _playCore.PlaySourceId,
            TryParseSource(_playCore.PlaySourceId, out var kind, out var sourceId) ? kind.ToString() : null,
            sourceId,
            queue.Select(CreateIdentity).ToList(),
            currentItem is null ? null : CreateIdentity(currentItem),
            currentIndex,
            Math.Max(0, (long)_state.Position.TotalMilliseconds),
            string.IsNullOrWhiteSpace(_state.ActiveStrategyId) ? null : _state.ActiveStrategyId,
            DateTimeOffset.UtcNow);

        await WriteStateAsync(state).ConfigureAwait(false);
    }

    private async Task WriteStateAsync(PlaybackMemoryState state)
    {
        var text = JsonSerializer.Serialize(state, JsonDefaults.Options);

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_localLibrarySettings.AdvancedMusicHistoryStorage)
            {
                var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(
                    MemoryFileName,
                    CreationCollisionOption.OpenIfExists);
                await FileIO.WriteTextAsync(file, text);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[MemorySettingsKey] = text;
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<PlaybackMemoryState?> ReadStateAsync()
    {
        try
        {
            string? text;
            if (_localLibrarySettings.AdvancedMusicHistoryStorage)
            {
                var file = await ApplicationData.Current.LocalCacheFolder.TryGetItemAsync(MemoryFileName);
                text = file is StorageFile storageFile
                    ? await FileIO.ReadTextAsync(storageFile)
                    : null;
            }
            else
            {
                text = ApplicationData.Current.LocalSettings.Values[MemorySettingsKey]?.ToString();
            }

            return string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize<PlaybackMemoryState>(text, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    private async Task<PlaybackMemoryState?> ReadLegacyStateAsync()
    {
        var legacy = await _history.GetCurrentPlayingListHistoryStateAsync().ConfigureAwait(false);
        if (legacy.Songs.Count == 0)
            return null;

        var currentIndex = legacy.CurrentIndex >= 0 && legacy.CurrentIndex < legacy.Songs.Count
            ? legacy.CurrentIndex
            : 0;

        return new PlaybackMemoryState(
            StateVersion,
            null,
            null,
            null,
            legacy.Songs.Select(CreateIdentity).ToList(),
            CreateIdentity(legacy.Songs[currentIndex]),
            currentIndex,
            0,
            _playbackSettings.ActiveStrategyId,
            DateTimeOffset.UtcNow);
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoring)
            return;

        if (e.PropertyName == nameof(PlaybackStateService.Position))
        {
            var bucket = (int)(_state.Position.TotalMilliseconds / SavePositionStepMilliseconds);
            if (bucket == _lastSavedPositionBucket)
                return;

            _lastSavedPositionBucket = bucket;
        }
        else if (e.PropertyName is not nameof(PlaybackStateService.NowPlayingProviderItem)
                 and not nameof(PlaybackStateService.NowPlayingIndex)
                 and not nameof(PlaybackStateService.QueueRevision)
                 and not nameof(PlaybackStateService.ActiveStrategyId))
        {
            return;
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;
        _ = SaveAfterDelayAsync(cts);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SaveDebounceMilliseconds, cts.Token).ConfigureAwait(false);
            await WriteCurrentStateAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_saveDebounceCts, cts))
                _saveDebounceCts = null;

            cts.Dispose();
        }
    }

    private static PlaybackItemIdentity CreateIdentity(SingleSongBase song)
    {
        return new PlaybackItemIdentity(
            song.ProviderId ?? string.Empty,
            song.TypeId ?? string.Empty,
            song.ActualId ?? string.Empty,
            song.ProviderId == LocalPlaybackProvider.ProviderIdValue ? song.ActualId : null);
    }

    private static int FindQueueIndex(IReadOnlyList<SingleSongBase> queue, PlaybackItemIdentity? identity)
    {
        if (identity is null)
            return -1;

        for (var i = 0; i < queue.Count; i++)
        {
            var song = queue[i];
            if (Matches(song, identity))
                return i;
        }

        return -1;
    }

    private static bool Matches(SingleSongBase song, PlaybackItemIdentity identity)
    {
        return string.Equals(song.ProviderId, identity.ProviderId, StringComparison.Ordinal)
               && string.Equals(song.TypeId, identity.TypeId, StringComparison.Ordinal)
               && string.Equals(song.ActualId, identity.ActualId, StringComparison.Ordinal);
    }

    private static string CreateKey(string? providerId, string? typeId, string? actualId)
    {
        return $"{providerId ?? string.Empty}\u001f{typeId ?? string.Empty}\u001f{actualId ?? string.Empty}";
    }

    private static TimeSpan GetRestorePosition(long positionMilliseconds, long durationMilliseconds)
    {
        if (positionMilliseconds <= 0)
            return TimeSpan.Zero;

        if (durationMilliseconds > 0
            && durationMilliseconds - positionMilliseconds <= EndPositionResetThresholdMilliseconds)
            return TimeSpan.Zero;

        return TimeSpan.FromMilliseconds(positionMilliseconds);
    }

    private static bool TryParseSource(PlaybackMemoryState memory, out SongListQueueScopeKind kind, out string id)
    {
        if (!string.IsNullOrWhiteSpace(memory.SourceKind)
            && Enum.TryParse(memory.SourceKind, out kind)
            && !string.IsNullOrWhiteSpace(memory.SourceId))
        {
            id = memory.SourceId;
            return IsRestorableKind(kind);
        }

        return TryParseSource(memory.PlaySourceId, out kind, out id);
    }

    private static bool TryParseSource(string? playSourceId, out SongListQueueScopeKind kind, out string id)
    {
        kind = default;
        id = string.Empty;

        if (string.IsNullOrWhiteSpace(playSourceId) || playSourceId.Length < 3)
            return false;

        var prefix = playSourceId[..2];
        id = playSourceId[2..];
        kind = prefix switch
        {
            QueueSourcePrefixes.Playlist => SongListQueueScopeKind.Playlist,
            QueueSourcePrefixes.Album => SongListQueueScopeKind.Album,
            QueueSourcePrefixes.Radio => SongListQueueScopeKind.Radio,
            QueueSourcePrefixes.DailyRecommend => SongListQueueScopeKind.DailyRecommend,
            _ => default
        };

        return IsRestorableKind(kind) && !string.IsNullOrWhiteSpace(id);
    }

    private static string? BuildPlaySourceId(SongListQueueScopeKind kind, string id)
    {
        return kind switch
        {
            SongListQueueScopeKind.Playlist => QueueSourcePrefixes.Playlist + id,
            SongListQueueScopeKind.Album => QueueSourcePrefixes.Album + id,
            SongListQueueScopeKind.Radio => QueueSourcePrefixes.Radio + id,
            SongListQueueScopeKind.DailyRecommend => QueueSourcePrefixes.DailyRecommend + id,
            _ => null
        };
    }

    private static bool IsRestorableKind(SongListQueueScopeKind kind)
    {
        return kind is SongListQueueScopeKind.Playlist
            or SongListQueueScopeKind.Album
            or SongListQueueScopeKind.Radio
            or SongListQueueScopeKind.DailyRecommend;
    }
}
