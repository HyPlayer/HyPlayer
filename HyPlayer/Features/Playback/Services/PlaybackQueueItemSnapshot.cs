using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
/// Playback queue row projection for UI surfaces that need display text and queue index.
/// </summary>
public sealed class PlaybackQueueItemSnapshot
{
    public PlaybackQueueItemSnapshot(
        int queueIndex,
        string name,
        string translation,
        string artistText,
        SingleSongBase? providerItem)
    {
        QueueIndex = queueIndex;
        Name = name;
        Translation = translation;
        ArtistText = artistText;
        ProviderItem = providerItem;
    }

    public int QueueIndex { get; }
    public string Name { get; }
    public string Translation { get; }
    public string ArtistText { get; }
    public SingleSongBase? ProviderItem { get; }
}
