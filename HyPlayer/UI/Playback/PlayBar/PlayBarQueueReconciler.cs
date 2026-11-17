using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using ObservableCollections;

namespace HyPlayer.UI.Playback.PlayBar;

internal static class PlayBarQueueReconciler
{
    internal static PlayBarQueueItem[] Match(
        IReadOnlyList<PlayBarQueueItem> existing,
        IReadOnlyList<PlaybackQueueItemSnapshot> snapshots)
    {
        var byReference = new Dictionary<SingleSongBase, Queue<PlayBarQueueItem>>(ReferenceEqualityComparer.Instance);
        var byIdentity = new Dictionary<(string, string, string), Queue<PlayBarQueueItem>>();
        foreach (var row in existing)
        {
            if (row.ProviderItem is not { } song) continue;
            if (!byReference.TryGetValue(song, out var referenceRows))
                byReference[song] = referenceRows = new();
            referenceRows.Enqueue(row);
            if (string.IsNullOrEmpty(song.ActualId)) continue;
            var key = (song.ProviderId, song.TypeId, song.ActualId);
            if (!byIdentity.TryGetValue(key, out var identityRows))
                byIdentity[key] = identityRows = new();
            identityRows.Enqueue(row);
        }

        var desired = new PlayBarQueueItem[snapshots.Count];
        var used = new HashSet<PlayBarQueueItem>();
        // Reserve exact references before identity fallbacks can consume those rows.
        for (var i = 0; i < snapshots.Count; i++)
            if (snapshots[i].ProviderItem is { } song
                && byReference.TryGetValue(song, out var matches) && matches.TryDequeue(out var row))
            {
                desired[i] = row;
                used.Add(row);
            }

        for (var i = 0; i < snapshots.Count; i++)
        {
            if (desired[i] is not null) continue;
            var snapshot = snapshots[i];
            if (snapshot.ProviderItem is { ActualId.Length: > 0 } song
                && byIdentity.TryGetValue((song.ProviderId, song.TypeId, song.ActualId), out var matches))
            {
                while (matches.TryDequeue(out var row))
                    if (used.Add(row)) { desired[i] = row; break; }
            }
            desired[i] ??= PlayBarQueueItem.FromSnapshot(snapshot, null);
        }
        return desired;
    }

    internal static async Task<bool> ApplyAsync(
        ObservableList<PlayBarQueueItem> rows,
        IReadOnlyList<PlaybackQueueItemSnapshot> snapshots,
        PlayBarQueueItem[] desired,
        Func<SingleSongBase?> nowPlaying,
        Func<bool> isCurrent,
        Func<Task>? yieldAsync = null)
    {
        var retained = desired.ToHashSet();
        var changes = 0;
        async Task YieldIfNeeded()
        {
            if (++changes % 32 != 0) return;
            if (yieldAsync is not null) await yieldAsync();
            else await Task.Yield();
        }

        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!isCurrent()) return false;
            if (retained.Contains(rows[i])) continue;
            rows.RemoveAt(i);
            await YieldIfNeeded();
        }
        var present = rows.ToHashSet();
        for (var i = 0; i < desired.Length; i++)
        {
            if (!isCurrent()) return false;
            var row = desired[i];
            if (i >= rows.Count || !ReferenceEquals(rows[i], row))
            {
                if (present.Add(row)) rows.Insert(i, row);
                else rows.Move(rows.IndexOf(row), i);
            }
            row.Update(snapshots[i], nowPlaying());
            await YieldIfNeeded();
        }
        return isCurrent();
    }
}
