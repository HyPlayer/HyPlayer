using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
namespace HyPlayer.UI.Playback.PlayBar;

public sealed class PlayBarQueueItem : ObservableObject
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

    private static bool IsSameSong(SingleSongBase? left, SingleSongBase? right) =>
        ReferenceEquals(left, right)
        || (left is not null
            && right is not null
            && left.ProviderId == right.ProviderId
            && left.TypeId == right.TypeId
            && left.ActualId == right.ActualId);
}
