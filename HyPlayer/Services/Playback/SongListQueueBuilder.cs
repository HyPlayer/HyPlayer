using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

internal sealed class SongListQueueBuilder(
    PlayCoreBase playCore,
    IPlaybackQueueLoader queueLoader,
    IPlaybackControlService control,
    PlaybackStateService state,
    INotificationService notification,
    IBackgroundTaskRunner taskRunner) : ISongListQueueBuilder
{
    private readonly object _queueBuildLock = new();
    private readonly SemaphoreSlim _queueMutationLock = new(1, 1);
    private CancellationTokenSource? _queueBuildCts;
    private string? _queueBuildSourceId;
    private SingleSongBase? _queueBuildTargetSong;

    public async Task BuildAndPlayAsync(SingleSongBase clickedSong, SongListQueueScope scope, IReadOnlyList<SingleSongBase> visibleSongs)
    {
        if (visibleSongs.Count == 0) return;

        var currentSongId = state.NowPlayingProviderItem?.ActualId;
        var shiftSong = clickedSong.ActualId == currentSongId && state.IsPlaying;
        var playSourceId = scope.ToPlaySourceId();

        if (!shiftSong)
        {
            if (playSourceId != null && playCore.PlaySourceId == playSourceId)
            {
                if (TryUpdatePendingQueueBuildTarget(playSourceId, clickedSong))
                {
                    if (!await TryLoadFromCurrentQueueAsync(clickedSong).ConfigureAwait(false))
                        await LoadIntoTemporaryQueueAsync(clickedSong).ConfigureAwait(false);
                    return;
                }

                if (await TryLoadFromCurrentQueueAsync(clickedSong).ConfigureAwait(false))
                    return;
            }

            CancelPendingQueueBuild();
            await LoadClickedSongFirstAsync(clickedSong, playSourceId).ConfigureAwait(false);
            var queueBuildCts = StartQueueBuildCancellation(playSourceId, clickedSong);
            taskRunner.Forget(
                BuildQueueAroundPlayingSongAsync(clickedSong, scope, visibleSongs, playSourceId, queueBuildCts),
                "build song list queue after first playback");
            return;
        }

        CancelPendingQueueBuild();

        if (scope.CanLoadCompleteSource)
        {
            await AppendCompleteSourceAsync(scope, visibleSongs.Count, !shiftSong);
        }
        else
        {
            await _queueMutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!shiftSong)
                    await playCore.StopAsync().ConfigureAwait(false);

                await playCore.RemoveAllSongAsync().ConfigureAwait(false);
                await playCore.InsertSongRangeAsync(visibleSongs.ToList()).ConfigureAwait(false);
            }
            finally
            {
                _queueMutationLock.Release();
            }
        }

        if (playSourceId != null)
            playCore.PlaySourceId = playSourceId;

        notification.ShowMessage("无感歌单切换", "成功无感切换到歌单");
        var nowPlayingIndex = state.NowPlayingIndex >= 0
            ? state.NowPlayingIndex
            : await playCore.GetCurrentIndexAsync().ConfigureAwait(false);
        if (nowPlayingIndex >= 0)
        {
            await playCore.MovePointerToIndexAsync(nowPlayingIndex).ConfigureAwait(false);
        }
    }

    private async Task LoadClickedSongFirstAsync(SingleSongBase clickedSong, string? playSourceId)
    {
        await _queueMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await playCore.StopAsync().ConfigureAwait(false);
            await playCore.RemoveAllSongAsync().ConfigureAwait(false);
            await playCore.InsertSongAsync(clickedSong).ConfigureAwait(false);
            await playCore.MovePointerToIndexAsync(0).ConfigureAwait(false);
            if (playSourceId != null)
                playCore.PlaySourceId = playSourceId;
        }
        finally
        {
            _queueMutationLock.Release();
        }

        await control.LoadAndPlayAsync(clickedSong, removeCurrentSongs: false).ConfigureAwait(false);
    }

    private async Task BuildQueueAroundPlayingSongAsync(
        SingleSongBase clickedSong,
        SongListQueueScope scope,
        IReadOnlyList<SingleSongBase> visibleSongs,
        string? playSourceId,
        CancellationTokenSource cancellationTokenSource)
    {
        var cancellationToken = cancellationTokenSource.Token;
        var lockTaken = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var songs = scope.CanLoadCompleteSource
                ? await LoadCompleteSourceSongsAsync(scope, cancellationToken).ConfigureAwait(false)
                : visibleSongs.ToList();
            cancellationToken.ThrowIfCancellationRequested();

            if (songs.Count == 0)
                return;

            await _queueMutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            await playCore.RemoveAllSongAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await playCore.InsertSongRangeAsync(songs, ctk: cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (playSourceId != null)
                playCore.PlaySourceId = playSourceId;

            var targetSong = GetPendingQueueBuildTarget(cancellationTokenSource) ?? clickedSong;
            var targetIndex = FindSongIndex(songs, targetSong);
            cancellationToken.ThrowIfCancellationRequested();
            if (targetIndex >= 0)
                await playCore.MovePointerToIndexAsync(targetIndex, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
                _queueMutationLock.Release();

            lock (_queueBuildLock)
            {
                if (ReferenceEquals(_queueBuildCts, cancellationTokenSource))
                {
                    _queueBuildCts = null;
                    _queueBuildSourceId = null;
                    _queueBuildTargetSong = null;
                }
            }

            cancellationTokenSource.Dispose();
        }
    }

    private async Task<List<SingleSongBase>> LoadCompleteSourceSongsAsync(
        SongListQueueScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.Id == null)
            return [];

        var result = await queueLoader.LoadSourceByKindAsync(scope.Kind, scope.Id, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return [];

        var songs = new List<SingleSongBase>();
        foreach (var batch in result.Batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            songs.AddRange(batch);
        }

        return songs;
    }

    private async Task AppendCompleteSourceAsync(SongListQueueScope scope, int availableVisibleCount, bool clearFirst)
    {
        await AppendCompleteSourceAsync(scope, availableVisibleCount, clearFirst, stopBeforeClear: true).ConfigureAwait(false);
    }

    private async Task AppendCompleteSourceAsync(
        SongListQueueScope scope,
        int availableVisibleCount,
        bool clearFirst,
        bool stopBeforeClear)
    {
        await AppendCompleteSourceAsync(
            scope,
            availableVisibleCount,
            clearFirst,
            stopBeforeClear,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task AppendCompleteSourceAsync(
        SongListQueueScope scope,
        int availableVisibleCount,
        bool clearFirst,
        bool stopBeforeClear,
        CancellationToken cancellationToken)
    {
        if (scope.Id == null) return;

        // Kind-based 路由 — 跳过 string 编码/解码，直接委托给 IQueueSourceProvider
        var playSourceId = scope.ToPlaySourceId();
        if (playSourceId != null
            && playCore.PlaySourceId == playSourceId
            && (await playCore.GetPlaylistAsync(cancellationToken).ConfigureAwait(false)).Count == availableVisibleCount)
            return;

        if (clearFirst)
        {
            if (stopBeforeClear)
                await playCore.StopAsync().ConfigureAwait(false);
            await playCore.RemoveAllSongAsync(cancellationToken).ConfigureAwait(false);
        }

        await queueLoader.AppendSourceByKindAsync(scope.Kind, scope.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryLoadFromCurrentQueueAsync(SingleSongBase clickedSong)
    {
        await _queueMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var queue = await playCore.GetPlaylistAsync().ConfigureAwait(false);
            var targetIndex = FindSongIndex(queue, clickedSong);
            if (targetIndex < 0)
                return false;

            await playCore.MovePointerToIndexAsync(targetIndex).ConfigureAwait(false);
        }
        finally
        {
            _queueMutationLock.Release();
        }

        if (playCore.CurrentSong is { } song)
            await control.LoadAndPlayAsync(song, removeCurrentSongs: false).ConfigureAwait(false);
        else
            await control.LoadAndPlayAsync(clickedSong, removeCurrentSongs: false).ConfigureAwait(false);

        return true;
    }

    private async Task LoadIntoTemporaryQueueAsync(SingleSongBase clickedSong)
    {
        await _queueMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var queue = await playCore.GetPlaylistAsync().ConfigureAwait(false);
            var targetIndex = FindSongIndex(queue, clickedSong);
            if (targetIndex < 0)
            {
                await playCore.InsertSongAsync(clickedSong).ConfigureAwait(false);
                queue = await playCore.GetPlaylistAsync().ConfigureAwait(false);
                targetIndex = FindSongIndex(queue, clickedSong);
            }

            if (targetIndex >= 0)
                await playCore.MovePointerToIndexAsync(targetIndex).ConfigureAwait(false);
        }
        finally
        {
            _queueMutationLock.Release();
        }

        if (playCore.CurrentSong is { } song)
            await control.LoadAndPlayAsync(song, removeCurrentSongs: false).ConfigureAwait(false);
        else
            await control.LoadAndPlayAsync(clickedSong, removeCurrentSongs: false).ConfigureAwait(false);
    }

    private void CancelPendingQueueBuild()
    {
        CancellationTokenSource? oldCts;

        lock (_queueBuildLock)
        {
            oldCts = _queueBuildCts;
            _queueBuildCts = null;
            _queueBuildSourceId = null;
            _queueBuildTargetSong = null;
        }

        try
        {
            oldCts?.Cancel();
        }
        catch (System.ObjectDisposedException)
        {
        }
    }

    private CancellationTokenSource StartQueueBuildCancellation(string? sourceId, SingleSongBase targetSong)
    {
        var newCts = new CancellationTokenSource();
        lock (_queueBuildLock)
        {
            _queueBuildCts = newCts;
            _queueBuildSourceId = sourceId;
            _queueBuildTargetSong = targetSong;
        }

        return newCts;
    }

    private bool TryUpdatePendingQueueBuildTarget(string sourceId, SingleSongBase targetSong)
    {
        lock (_queueBuildLock)
        {
            if (_queueBuildCts is null
                || _queueBuildCts.IsCancellationRequested
                || _queueBuildSourceId != sourceId)
                return false;

            _queueBuildTargetSong = targetSong;
            return true;
        }
    }

    private SingleSongBase? GetPendingQueueBuildTarget(CancellationTokenSource cancellationTokenSource)
    {
        lock (_queueBuildLock)
        {
            return ReferenceEquals(_queueBuildCts, cancellationTokenSource)
                ? _queueBuildTargetSong
                : null;
        }
    }

    private static int FindSongIndex(IReadOnlyList<SingleSongBase> songs, SingleSongBase targetSong)
    {
        for (var i = 0; i < songs.Count; i++)
        {
            var song = songs[i];
            if (song.ProviderId == targetSong.ProviderId
                && song.TypeId == targetSong.TypeId
                && song.ActualId == targetSong.ActualId)
                return i;
        }

        return -1;
    }
}
