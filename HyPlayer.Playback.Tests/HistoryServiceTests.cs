using HyPlayer.Features.History.Services;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class HistoryServiceTests
{
    [Test]
    public void Stored_song_history_ids_are_normalized_for_provider_range_lookup()
    {
        var providerIds = HistoryService.BuildProviderSongIds(
            new[] { "123", "sg456", "", "789" },
            "sg");

        Ensure(providerIds.SequenceEqual(new[] { "sg123", "sg456", "sg789" }),
            "Raw and already-prefixed history IDs must produce valid provider song IDs without duplication.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
