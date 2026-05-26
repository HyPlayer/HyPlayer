using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

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

    public static PlayBarQueueItem FromQueueItem(
        int queueIndex,
        SingleSongBase? providerItem,
        HyPlayItem? legacyItem,
        int nowPlayingIndex)
    {
        if (providerItem is not null)
        {
            return new PlayBarQueueItem(
                queueIndex,
                providerItem.Name ?? string.Empty,
                providerItem is IHasTranslation translatedProvider ? translatedProvider.Translation ?? string.Empty : string.Empty,
                providerItem.CreatorList is { Count: > 0 } creators ? string.Join("; ", creators) : string.Empty,
                providerItem,
                queueIndex == nowPlayingIndex);
        }

        return new PlayBarQueueItem(
            queueIndex,
            legacyItem?.Name ?? string.Empty,
            legacyItem?.Translation ?? string.Empty,
            legacyItem?.ArtistString ?? string.Empty,
            null,
            queueIndex == nowPlayingIndex);
    }
}
