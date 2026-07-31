using System.Threading;
using System.Threading.Tasks;
using Depository.Abstraction.Interfaces.NotificationHub;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;

namespace HyPlayer.Features.Playback.Services;

public sealed class PlayCoreStateSynchronizer(
    PlayCoreBase playCore,
    PlaybackStateService state) :
    INotificationSubscriber<CurrentSongChangedNotification>,
    INotificationSubscriber<OrderedPlaylistChangedNotification>,
    INotificationSubscriber<InnerPlayListChangedNotification>
{
    public async Task HandleNotificationAsync(CurrentSongChangedNotification notification,
        CancellationToken ctk = new())
    {
        if (!ReferenceEquals(state.NowPlayingProviderItem, notification.CurrentPlayingSong))
            state.SetNowPlaying(notification.CurrentPlayingSong);

        state.NowPlayingIndex = await playCore.GetCurrentIndexAsync(ctk).ConfigureAwait(false);
    }

    public Task HandleNotificationAsync(InnerPlayListChangedNotification notification, CancellationToken ctk = new())
    {
        state.PublishQueueChanged();
        return Task.CompletedTask;
    }

    public async Task HandleNotificationAsync(OrderedPlaylistChangedNotification notification,
        CancellationToken ctk = new())
    {
        state.NowPlayingIndex = await playCore.GetCurrentIndexAsync(ctk).ConfigureAwait(false);
        if (!ReferenceEquals(state.NowPlayingProviderItem, playCore.CurrentSong))
            state.SetNowPlaying(playCore.CurrentSong);
        state.PublishQueueChanged(notification.IsRandom);
    }
}