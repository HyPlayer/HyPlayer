using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.Services;

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
    private readonly SemaphoreSlim _queueRequestLock = new(1, 1);
    private CancellationTokenSource? _queueBuildCts;
    private string? _queueBuildSourceId;
    private SingleSongBase? _queueBuildTargetSong;

    public async Task BuildAndPlayAsync(SingleSongBase clickedSong, SongListQueueScope scope,
        IReadOnlyList<SingleSongBase> visibleSongs)
    {
        if (visibleSongs.Count == 0) return;

        await _queueRequestLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await BuildAndPlayCoreAsync(clickedSong, scope, visibleSongs).ConfigureAwait(false);
        }
        finally
        {
            _queueRequestLock.Release();
        }
    }

    private async Task BuildAndPlayCoreAsync(
        SingleSongBase clickedSong,
        SongListQueueScope scope,
        IReadOnlyList<SingleSongBase> visibleSongs)
    {
        var shiftSong = SameSong(clickedSong, state.NowPlayingProviderItem) && state.IsPlaying;
        var playSourceId = scope.ToPlaySourceId();

        if (shiftSong
            && playSourceId != null
            && playCore.PlaySourceId == playSourceId)
            return;

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
        var replacementSongs = scope.CanLoadCompleteSource
            ? await LoadCompleteSourceSongsAsync(scope, CancellationToken.None).ConfigureAwait(false)
            : visibleSongs.ToList();
        if (replacementSongs.Count == 0)
            return;

        await _queueMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var replaced = await control
                .ReplaceQueueKeepingPlaybackAsync(replacementSongs, clickedSong, playSourceId)
                .ConfigureAwait(false);
            if (!replaced)
                return;
        }
        finally
        {
            _queueMutationLock.Release();
        }

        notification.ShowMessage("无感歌单切换", "成功无感切换到歌单");
    }

    private async Task LoadClickedSongFirstAsync(SingleSongBase clickedSong, string? playSourceId)
    {
        await _queueMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await control.StopAsync().ConfigureAwait(false);
            await control.ClearQueueAsync().ConfigureAwait(false);
            await playCore.InsertSongAsync(clickedSong).ConfigureAwait(false);
            await playCore.MovePointerToAsync(clickedSong).ConfigureAwait(false);
            playCore.PlaySourceId = playSourceId ?? string.Empty;
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
            var targetSong = GetPendingQueueBuildTarget(cancellationTokenSource) ?? clickedSong;
            cancellationToken.ThrowIfCancellationRequested();
            await control
                .ReplaceQueueKeepingPlaybackAsync(songs, targetSong, playSourceId, cancellationToken)
                .ConfigureAwait(false);
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

        var result = await queueLoader.LoadSourceByKindAsync(scope.Kind, scope.Id, cancellationToken)
            .ConfigureAwait(false);
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

    private async Task<bool> TryLoadFromCurrentQueueAsync(SingleSongBase clickedSong)
    {
        await _queueMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var queue = await playCore.GetPlaylistAsync().ConfigureAwait(false);
            var targetIndex = FindSongIndex(queue, clickedSong);
            if (targetIndex < 0)
                return false;

            await playCore.MovePointerToAsync(clickedSong).ConfigureAwait(false);
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
                await playCore.MovePointerToAsync(clickedSong).ConfigureAwait(false);
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
        catch (ObjectDisposedException)
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

    private static bool SameSong(SingleSongBase? left, SingleSongBase? right)
    {
        return ReferenceEquals(left, right)
               || (left is not null
                   && right is not null
                   && left.ProviderId == right.ProviderId
                   && left.TypeId == right.TypeId
                   && left.ActualId == right.ActualId);
    }
}