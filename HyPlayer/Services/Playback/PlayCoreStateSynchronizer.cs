using Depository.Abstraction.Interfaces.NotificationHub;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.Services.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

public sealed class PlayCoreStateSynchronizer(
    PlayCoreBase playCore,
    PlaybackStateService state,
    ILyricService lyricService,
    IPlaybackNotificationService playbackNotification,
    IBackgroundTaskRunner taskRunner) :
    INotificationSubscriber<CurrentSongChangedNotification>,
    INotificationSubscriber<OrderedPlaylistChangedNotification>,
    INotificationSubscriber<InnerPlayListChangedNotification>
{
    public async Task HandleNotificationAsync(CurrentSongChangedNotification notification, CancellationToken ctk = new())
    {
        state.SetNowPlaying(notification.CurrentPlayingSong);
        state.NowPlayingIndex = await playCore.GetCurrentIndexAsync(ctk).ConfigureAwait(false);
        if (notification.CurrentPlayingSong is { } song)
        {
            taskRunner.Forget(lyricService.LoadLyricsAsync(song, ctk), "load lyrics for PlayCore current song");
            taskRunner.Forget(playbackNotification.OnTrackChangedAsync(song), "update playback notification for PlayCore current song");
        }
    }

    public async Task HandleNotificationAsync(OrderedPlaylistChangedNotification notification, CancellationToken ctk = new())
    {
        state.NowPlayingIndex = await playCore.GetCurrentIndexAsync(ctk).ConfigureAwait(false);
        if (!ReferenceEquals(state.NowPlayingProviderItem, playCore.CurrentSong))
            state.SetNowPlaying(playCore.CurrentSong);
        state.PublishQueueChanged(notification.IsRandom);
    }

    public Task HandleNotificationAsync(InnerPlayListChangedNotification notification, CancellationToken ctk = new())
    {
        state.PublishQueueChanged();
        return Task.CompletedTask;
    }
}
