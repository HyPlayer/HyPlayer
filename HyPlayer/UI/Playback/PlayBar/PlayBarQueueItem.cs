using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.UI.Playback.PlayBar;

public sealed class PlayBarQueueItem
{
    public PlayBarQueueItem(
        int queueIndex,
        string name,
        string translation,
        string artistText,
        SingleSongBase? providerItem,
        bool isCurrent)
    {
        QueueIndex = queueIndex;
        Name = name;
        Translation = translation;
        ArtistText = artistText;
        ProviderItem = providerItem;
        IsCurrent = isCurrent;
    }

    public int QueueIndex { get; }
    public string Name { get; }
    public string Translation { get; }
    public string ArtistText { get; }
    public SingleSongBase? ProviderItem { get; }
    public bool IsCurrent { get; }

    public static PlayBarQueueItem FromSnapshot(PlaybackQueueItemSnapshot snapshot, int nowPlayingIndex)
    {
        return new PlayBarQueueItem(
            snapshot.QueueIndex,
            snapshot.Name,
            snapshot.Translation,
            snapshot.ArtistText,
            snapshot.ProviderItem,
            snapshot.QueueIndex == nowPlayingIndex);
    }
}
