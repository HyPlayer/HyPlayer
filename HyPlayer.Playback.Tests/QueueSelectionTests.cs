using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UI.Playback.PlayBar;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class QueueSelectionTests
{
    [Test]
    public void Queue_row_matches_current_song_by_identity_instead_of_queue_index()
    {
        var queuedSong = new FakeSong("selected");
        var currentSong = new FakeSong("selected");
        var snapshot = new PlaybackQueueItemSnapshot(
            queueIndex: 7,
            name: "Selected song",
            translation: string.Empty,
            artistText: string.Empty,
            providerItem: queuedSong);

        var row = PlayBarQueueItem.FromSnapshot(snapshot, currentSong);

        Ensure(row.IsCurrent, "Shuffle can change the ordered index without changing the selected song.");
        Ensure(row.QueueIndex == 7, "The row must retain its original queue index for queue mutations.");
    }

    [Test]
    public void Queue_row_does_not_match_a_different_song_at_the_same_position()
    {
        var snapshot = new PlaybackQueueItemSnapshot(
            queueIndex: 0,
            name: "First song",
            translation: string.Empty,
            artistText: string.Empty,
            providerItem: new FakeSong("first"));

        var row = PlayBarQueueItem.FromSnapshot(snapshot, new FakeSong("shuffled-first"));

        Ensure(!row.IsCurrent, "A shuffled position must not make a different song current.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeSong : SingleSongBase
    {
        public FakeSong(string id)
        {
            ActualId = id;
            Name = id;
        }

        public override string ProviderId => "test";
        public override string TypeId => "song";

        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) =>
            Task.FromResult<List<PersonBase>?>([]);
    }
}
