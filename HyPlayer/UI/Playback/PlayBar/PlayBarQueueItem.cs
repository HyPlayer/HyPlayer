using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.UI.Playback.PlayBar;

public sealed partial class PlayBarQueueItem : ObservableObject
{
    private bool _isCurrent;

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
        _isCurrent = isCurrent;
    }

    public int QueueIndex { get; }
    public string Name { get; }
    public string Translation { get; }
    public string ArtistText { get; }
    public SingleSongBase? ProviderItem { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public static PlayBarQueueItem FromSnapshot(
        PlaybackQueueItemSnapshot snapshot,
        SingleSongBase? nowPlayingItem)
    {
        return new PlayBarQueueItem(
            snapshot.QueueIndex,
            snapshot.Name,
            snapshot.Translation,
            snapshot.ArtistText,
            snapshot.ProviderItem,
            IsSameSong(snapshot.ProviderItem, nowPlayingItem));
    }

    private static bool IsSameSong(SingleSongBase? left, SingleSongBase? right)
    {
        return ReferenceEquals(left, right)
               || (left is not null
                   && right is not null
                   && left.ProviderId == right.ProviderId
                   && left.TypeId == right.TypeId
                   && left.ActualId == right.ActualId);
    }
}
