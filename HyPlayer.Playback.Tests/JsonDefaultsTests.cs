using System.Text.Json;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Serialization;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class JsonDefaultsTests
{
    [Test]
    public void HistoryState_ShouldRoundTripWithExistingNamingAndCaseInsensitiveReads()
    {
        var original = new CurPlayingListHistoryState(["ncm:123", "local:路径"], 1);
        var json = JsonDefaults.Serialize(original);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("songIds", out _))
            throw new Exception("History JSON lost its camelCase naming policy.");
        var restored = JsonDefaults.Deserialize<CurPlayingListHistoryState>(json);
        if (restored is null || restored.CurrentIndex != 1 || !restored.SongIds.SequenceEqual(original.SongIds))
            throw new Exception("History state did not round-trip.");
        restored = JsonDefaults.Deserialize<CurPlayingListHistoryState>("{\"SongIds\":[\"123\"],\"CurrentIndex\":0}");
        if (restored is null || restored.SongIds.Single() != "123")
            throw new Exception("Legacy case-insensitive history data no longer loads.");
    }

    [Test]
    public void PlaybackState_ShouldPreserveQueueIdentityAndOptionalFields()
    {
        var identity = new PlaybackItemIdentity("ncm", "song", "123");
        var savedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var original = new PlaybackMemoryState(1, "playlist", "ncm", "45", [identity], identity,
            0, 43210, "seq", savedAt, "lossless", "auto");
        var restored = JsonDefaults.Deserialize<PlaybackMemoryState>(JsonDefaults.Serialize(original));
        if (restored is null || restored.CurrentItem != identity || restored.Queue.Single() != identity ||
            restored.PositionMilliseconds != original.PositionMilliseconds || restored.SavedAt != savedAt ||
            restored.QualityRequest != "auto")
            throw new Exception("Playback persistence lost queue, position, timestamp or quality data.");
    }

    [Test]
    public void UnregisteredType_ShouldFailWithoutReflectionFallback()
    {
        try
        {
            JsonDefaults.TypeInfo<UnregisteredPayload>();
            throw new Exception("Unregistered payload unexpectedly resolved via reflection.");
        }
        catch (NotSupportedException)
        {
        }
    }

    private sealed record UnregisteredPayload(string Value);
}
