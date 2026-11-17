using System.Collections.Specialized;
using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UI.Playback.PlayBar;
using ObservableCollections;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class PlayBarQueueReconcilerTests
{
    [Test]
    public async Task Large_queue_append_creates_only_new_rows_and_identical_snapshot_is_silent()
    {
        var songs = Enumerable.Range(0, 10000).Select(i => new Song(i.ToString())).ToArray();
        var snapshots = PlayCoreQueueSnapshot.Build(songs);
        var rows = new ObservableList<PlayBarQueueItem>(snapshots.Select(s => PlayBarQueueItem.FromSnapshot(s, null)));
        var original = rows.ToArray();
        using var view = rows.ToNotifyCollectionChanged();
        var actions = new List<NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, args) => actions.Add(args.Action);
        var updated = PlayCoreQueueSnapshot.Build(songs.Append(new Song("new")).ToArray());
        var desired = PlayBarQueueReconciler.Match(original, updated);
        Check(desired.Take(original.Length).SequenceEqual(original), "Append recreated old rows.");
        await Apply(rows, updated, desired);
        Check(actions.SequenceEqual(new[] { NotifyCollectionChangedAction.Add }), "Append changed existing rows.");

        actions.Clear();
        var propertyChanges = 0;
        foreach (var row in rows) row.PropertyChanged += (_, _) => propertyChanges++;
        await Apply(rows, updated, PlayBarQueueReconciler.Match(rows.ToArray(), updated));
        Check(actions.Count == 0 && propertyChanges == 0, "An identical snapshot must be silent.");
    }

    [Test]
    public async Task Insert_delete_reorder_and_metadata_changes_reuse_existing_rows()
    {
        var a = new Song("a");
        var b = new Song("b");
        var c = new Song("c");
        var rows = new ObservableList<PlayBarQueueItem>();
        var first = PlayCoreQueueSnapshot.Build(new[] { a, b, c });
        await Apply(rows, first, PlayBarQueueReconciler.Match([], first));
        var aRow = rows[0];
        var cRow = rows[2];
        c.Name = "Updated";
        var updated = PlayCoreQueueSnapshot.Build(new[] { c, new Song("inserted"), a });
        using var view = rows.ToNotifyCollectionChanged();
        var actions = new List<NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, args) => actions.Add(args.Action);
        await Apply(rows, updated, PlayBarQueueReconciler.Match(rows.ToArray(), updated));
        Check(ReferenceEquals(rows[0], cRow) && ReferenceEquals(rows[2], aRow), "Reorder recreated rows.");
        Check(cRow.Name == "Updated" && aRow.QueueIndex == 2, "Reused metadata/index was stale.");
        Check(actions.Contains(NotifyCollectionChangedAction.Move) && !actions.Contains(NotifyCollectionChangedAction.Reset), "Reorder reset the queue.");
        await Apply(rows, [], []);
        Check(rows.Count == 0 && !actions.Contains(NotifyCollectionChangedAction.Reset), "Empty queue should remove remaining rows.");
    }

    [Test]
    public void Matching_reserves_references_and_handles_duplicate_and_empty_identifiers()
    {
        var first = new Song("same");
        var second = new Song("same");
        var empty = new Song("");
        var snapshots = PlayCoreQueueSnapshot.Build(new[] { first, second, empty });
        var existing = snapshots.Select(s => PlayBarQueueItem.FromSnapshot(s, null)).ToArray();
        var updated = PlayCoreQueueSnapshot.Build(new[] { new Song("same"), first, new Song("") });
        var desired = PlayBarQueueReconciler.Match(existing, updated);
        Check(ReferenceEquals(desired[1], existing[0]) && ReferenceEquals(desired[0], existing[1]), "Identity fallback stole an exact reference.");
        Check(!ReferenceEquals(desired[2], existing[2]) && desired.Distinct().Count() == 3, "Empty IDs or duplicates reused a row twice.");
    }

    [Test]
    public async Task Superseded_update_stops_at_batch_boundary_and_next_snapshot_reconciles_partial_state()
    {
        var snapshots = PlayCoreQueueSnapshot.Build(Enumerable.Range(0, 100).Select(i => new Song(i.ToString())).ToArray());
        var rows = new ObservableList<PlayBarQueueItem>();
        var current = true;
        var completed = await PlayBarQueueReconciler.ApplyAsync(rows, snapshots,
            PlayBarQueueReconciler.Match([], snapshots), () => null, () => current,
            () => { current = false; return Task.CompletedTask; });
        Check(!completed && rows.Count == 32, "A stale update continued after yielding.");
        var latest = snapshots.Reverse().ToArray();
        await Apply(rows, latest, PlayBarQueueReconciler.Match(rows.ToArray(), latest));
        Check(rows.Select(r => r.ProviderItem).SequenceEqual(latest.Select(s => s.ProviderItem)), "Latest snapshot did not repair partial state.");
    }

    private static Task<bool> Apply(ObservableList<PlayBarQueueItem> rows, PlaybackQueueItemSnapshot[] snapshots, PlayBarQueueItem[] desired) =>
        PlayBarQueueReconciler.ApplyAsync(rows, snapshots, desired, () => null, () => true, () => Task.CompletedTask);

    private sealed class Song : SingleSongBase
    {
        public Song(string id) { ActualId = id; Name = id; }
        public override string ProviderId => "test";
        public override string TypeId => "song";
        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) => Task.FromResult<List<PersonBase>?>([]);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
