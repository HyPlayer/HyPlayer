using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UI.Lists;
using TUnit.Core;
using Windows.Foundation;

namespace HyPlayer.Playback.Tests;

public sealed class SongListQueueBuilderTests
{
    [Test]
    public async Task Seamless_source_change_replaces_queue_and_keeps_current_song()
    {
        var first = new FakeSong("first");
        var current = new FakeSong("current");
        var oldNext = new FakeSong("old-next");
        var albumFirst = new FakeSong("album-first");
        var albumCurrent = new FakeSong("current");
        var albumNext = new FakeSong("album-next");
        var state = new PlaybackStateService { IsPlaying = true };
        state.SetNowPlaying(current);
        var playCore = new FakePlayCore(new SingleSongBase[] { first, current, oldNext }, 1) { PlaySourceId = "pl-old" };
        var loader = new FakeQueueLoader(ProviderQueueSourceLoadResult.FromSongs(
            new SingleSongBase[] { albumFirst, albumCurrent, albumNext }));
        var control = new FakePlaybackControl(playCore, state);
        var notification = new FakeNotificationService();
        var runner = new FakeBackgroundTaskRunner();
        var builder = new SongListQueueBuilder(playCore, loader, control, state, notification, runner);

        await builder.BuildAndPlayAsync(
            albumCurrent,
            SongListQueueScope.Album("album"),
            new SingleSongBase[] { albumFirst, albumCurrent, albumNext });

        Ensure(playCore.Queue.Select(song => song.ActualId).SequenceEqual(
                new string?[] { "album-first", "current", "album-next" }),
            "A seamless source change must replace the old queue instead of appending the album.");
        Ensure(playCore.CurrentIndex == 1, "The pointer must target the matching song in the replacement queue.");
        Ensure(ReferenceEquals(playCore.CurrentSong, current),
            "Seamless replacement must preserve the active song instance associated with the audio ticket.");
        Ensure(playCore.PlaySourceId == "alalbum", "The replacement source id must be published.");
        Ensure(control.ReplaceQueueCount == 1, "The queue must be replaced through the playback-safe operation.");
        Ensure(control.StopCount == 0 && control.LoadAndPlayCount == 0,
            "Seamless replacement must not stop or reload the playing audio source.");
        Ensure(notification.Messages.Count == 1, "A successful seamless switch should notify once.");
    }

    [Test]
    public async Task Repeated_click_does_not_cancel_pending_complete_queue_build()
    {
        var current = new FakeSong("current");
        var next = new FakeSong("next");
        var state = new PlaybackStateService();
        var playCore = new FakePlayCore(Array.Empty<SingleSongBase>(), -1);
        var loader = new FakeQueueLoader(ProviderQueueSourceLoadResult.FromSongs(
            new SingleSongBase[] { current, next }))
        {
            LoadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var control = new FakePlaybackControl(playCore, state);
        var runner = new FakeBackgroundTaskRunner();
        var builder = new SongListQueueBuilder(
            playCore,
            loader,
            control,
            state,
            new FakeNotificationService(),
            runner);
        var scope = SongListQueueScope.Playlist("playlist");

        await builder.BuildAndPlayAsync(current, scope, new SingleSongBase[] { current });
        await builder.BuildAndPlayAsync(current, scope, new SingleSongBase[] { current });

        Ensure(loader.LoadCount == 1, "The second click must reuse the pending complete-source build.");
        Ensure(runner.Tasks.Count == 1, "Only one background queue build should be scheduled.");

        loader.LoadGate.SetResult();
        await runner.Tasks[0];

        Ensure(playCore.Queue.Select(song => song.ActualId).SequenceEqual(new string?[] { "current", "next" }),
            "The original background build must complete with the full provider queue.");
    }

    [Test]
    public async Task Repeated_click_on_current_song_in_same_source_is_a_no_op()
    {
        var current = new FakeSong("current");
        var next = new FakeSong("next");
        var state = new PlaybackStateService { IsPlaying = true };
        state.SetNowPlaying(current);
        var playCore = new FakePlayCore(new SingleSongBase[] { current, next }, 0) { PlaySourceId = "plplaylist" };
        var loader = new FakeQueueLoader(ProviderQueueSourceLoadResult.FromSongs(
            new SingleSongBase[] { current, next }));
        var control = new FakePlaybackControl(playCore, state);
        var notification = new FakeNotificationService();
        var builder = new SongListQueueBuilder(
            playCore,
            loader,
            control,
            state,
            notification,
            new FakeBackgroundTaskRunner());

        await builder.BuildAndPlayAsync(
            current,
            SongListQueueScope.Playlist("playlist"),
            new SingleSongBase[] { current, next });
        await builder.BuildAndPlayAsync(
            current,
            SongListQueueScope.Playlist("playlist"),
            new SingleSongBase[] { current, next });

        Ensure(loader.LoadCount == 0, "Repeated clicks in the same source must not reload the playlist.");
        Ensure(control.ReplaceQueueCount == 0, "Repeated clicks in the same source must not rebuild or reposition the queue.");
        Ensure(playCore.CurrentIndex == 0, "Repeated clicks must keep the current ordered-list index.");
        Ensure(notification.Messages.Count == 0, "A no-op click must not report a seamless source switch.");
    }

    [Test]
    public async Task First_song_build_completes_when_mirrored_playing_status_lags()
    {
        var first = new FakeSong("first");
        var second = new FakeSong("second");
        var state = new PlaybackStateService();
        var playCore = new FakePlayCore(Array.Empty<SingleSongBase>(), -1);
        var loader = new FakeQueueLoader(ProviderQueueSourceLoadResult.FromSongs(
            new SingleSongBase[] { first, second }));
        var control = new FakePlaybackControl(playCore, state) { MirrorPlayingOnLoad = false };
        var runner = new FakeBackgroundTaskRunner();
        var builder = new SongListQueueBuilder(
            playCore,
            loader,
            control,
            state,
            new FakeNotificationService(),
            runner);

        await builder.BuildAndPlayAsync(
            first,
            SongListQueueScope.Playlist("playlist"),
            new SingleSongBase[] { first });
        await runner.Tasks.Single();

        Ensure(!state.IsPlaying, "The test must preserve the intentionally lagging mirrored status.");
        Ensure(control.ReplaceQueueCount == 1,
            "A stale mirrored status must not reject the complete queue replacement.");
        Ensure(playCore.Queue.Select(song => song.ActualId).SequenceEqual(new string?[] { "first", "second" }),
            "The first song must receive the complete queue so natural advance can reach the second song.");
        Ensure(ReferenceEquals(playCore.CurrentSong, first),
            "Completing the first queue must keep the song instance associated with active playback.");
    }

    [Test]
    public void Cold_start_queue_replacement_does_not_require_a_published_playback_source()
    {
        var clickedSong = new FakeSong("first");
        var equivalentQueueSong = new FakeSong("first");

        Ensure(
            PlaybackControlService.CanReplaceQueueForCurrentSong(
                clickedSong,
                equivalentQueueSong,
                clickedSong),
            "Cold-start queue completion must be accepted once the logical current song and queue pointer agree.");

        Ensure(
            !PlaybackControlService.CanReplaceQueueForCurrentSong(
                new FakeSong("other"),
                equivalentQueueSong,
                clickedSong),
            "A stale background build must still be rejected after the current song changes.");

        var completedQueue = PlaybackControlService.CreateQueuePreservingCurrentSong(
            new SingleSongBase[] { equivalentQueueSong, new FakeSong("second") },
            clickedSong);
        Ensure(ReferenceEquals(completedQueue[0], clickedSong),
            "Queue completion must retain the exact song object owned by the active audio ticket.");
    }

    [Test]
    public void Effective_scope_uses_visible_songs_only_for_an_active_filter()
    {
        var playlistScope = SongListQueueScope.Playlist("playlist");

        Ensure(ReferenceEquals(
                ContainerItemsView.ResolveEffectiveQueueScope(playlistScope, string.Empty),
                playlistScope),
            "An unfiltered list must retain its complete provider scope.");
        Ensure(ReferenceEquals(
                ContainerItemsView.ResolveEffectiveQueueScope(playlistScope, "needle"),
                SongListQueueScope.Visible),
            "An active filter must use the visible-song scope.");
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
            Available = true;
        }

        public override string ProviderId => "test";
        public override string TypeId => "song";
        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) =>
            Task.FromResult<List<PersonBase>?>([]);
    }

    private sealed class FakeQueueLoader(ProviderQueueSourceLoadResult result) : IPlaybackQueueLoader
    {
        public int LoadCount { get; private set; }
        public TaskCompletionSource? LoadGate { get; init; }

        public async Task<ProviderQueueSourceLoadResult> LoadSourceByKindAsync(
            SongListQueueScopeKind kind,
            string id,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            if (LoadGate is not null)
                await LoadGate.Task.WaitAsync(cancellationToken);
            return result;
        }

        public Task<bool> AppendNcSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> AppendSourceByKindAsync(
            SongListQueueScopeKind kind,
            string id,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> AppendRadioListAsync(
            string radioId,
            bool asc = false,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> AppendSongsAsync(
            IEnumerable<SingleSongBase> songs,
            bool skipDuplicateSingle = false,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FakePlaybackControl(FakePlayCore playCore, PlaybackStateService state) : IPlaybackControlService
    {
        public event EventHandler<SeekRequestedEventArgs>? SeekRequested;
        public bool IsPlaying => state.IsPlaying;
        public TimeSpan Position => state.Position;
        public double Volume { get; set; }
        public int StopCount { get; private set; }
        public int LoadAndPlayCount { get; private set; }
        public int ReplaceQueueCount { get; private set; }
        public bool MirrorPlayingOnLoad { get; init; } = true;

        public Task SetTransitionAsync(string transitionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetPlayModeAsync(string playModeId, CancellationToken ct = default) => Task.CompletedTask;
        public void SetAudioGainEnabled(bool enabled) { }

        public Task ClearQueueAsync(CancellationToken ct = default)
        {
            playCore.ClearQueue();
            return Task.CompletedTask;
        }

        public async Task<bool> ReplaceQueueKeepingPlaybackAsync(
            IReadOnlyList<SingleSongBase> songs,
            SingleSongBase expectedCurrentSong,
            string? playSourceId,
            CancellationToken ct = default)
        {
            if (!SameSong(state.NowPlayingProviderItem, expectedCurrentSong))
                return false;

            var targetIndex = songs.ToList().FindIndex(song => SameSong(song, expectedCurrentSong));
            if (targetIndex < 0)
                return false;

            ReplaceQueueCount++;
            var currentSong = playCore.CurrentSong ?? expectedCurrentSong;
            var replacementSongs = PlaybackControlService.CreateQueuePreservingCurrentSong(songs, currentSong);
            await playCore.ReplaceQueueAsync(replacementSongs, targetIndex, ct);
            playCore.PlaySourceId = playSourceId ?? string.Empty;
            state.SetNowPlaying(playCore.CurrentSong);
            return true;
        }

        public Task SeekAsync(TimeSpan target)
        {
            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(target));
            return Task.CompletedTask;
        }

        public void Play() => state.IsPlaying = true;
        public void Pause() => state.IsPlaying = false;

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            state.IsPlaying = false;
            return Task.CompletedTask;
        }

        public void TogglePlayPause() => state.IsPlaying = !state.IsPlaying;

        public Task LoadAndPlayAsync(SingleSongBase song, bool autoPlay = true, bool removeCurrentSongs = true)
        {
            LoadAndPlayCount++;
            state.SetNowPlaying(song);
            state.IsPlaying = autoPlay && MirrorPlayingOnLoad;
            return Task.CompletedTask;
        }

        public Task MoveNextAndPlayAsync(bool userInitiated) => Task.CompletedTask;
        public Task MovePreviousAndPlayAsync() => Task.CompletedTask;
        public Task InitializeAsync() => Task.CompletedTask;

        private static bool SameSong(SingleSongBase? left, SingleSongBase? right) =>
            ReferenceEquals(left, right)
            || (left is not null
                && right is not null
                && left.ProviderId == right.ProviderId
                && left.TypeId == right.TypeId
                && left.ActualId == right.ActualId);
    }

    private sealed class FakeBackgroundTaskRunner : IBackgroundTaskRunner
    {
        public List<Task> Tasks { get; } = [];

        public void Forget(Task task, string operationName) => Tasks.Add(task);
        public void Forget(IAsyncAction action, string operationName) { }
        public void Forget(Func<Task> taskFactory, string operationName) => Tasks.Add(taskFactory());
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<(string Title, string? Message)> Messages { get; } = [];
        public void ShowMessage(string title, string? message = null) => Messages.Add((title, message));
    }

    private sealed class FakePlayCore : PlayCoreBase
    {
        private readonly FakePlayListManager _manager;

        public FakePlayCore(IEnumerable<SingleSongBase> songs, int currentIndex)
        {
            Queue = songs.ToList();
            CurrentIndex = currentIndex;
            CurrentSong = currentIndex >= 0 && currentIndex < Queue.Count ? Queue[currentIndex] : null;
            _manager = new FakePlayListManager(this);
            CurrentPlayList = _manager;
        }

        public List<SingleSongBase> Queue { get; private set; }
        public int CurrentIndex { get; private set; }

        public void ClearQueue()
        {
            Queue = [];
            CurrentIndex = -1;
        }

        public Task ReplaceQueueAsync(
            IReadOnlyList<SingleSongBase> songs,
            int currentIndex,
            CancellationToken cancellationToken)
        {
            Queue = songs.ToList();
            CurrentIndex = currentIndex;
            CurrentSong = Queue[currentIndex];
            return Task.CompletedTask;
        }

        public override Task RegisterAudioServiceAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task RegisterMusicProviderAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task RegisterPlayListControllerAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task UnregisterAudioServiceAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task UnregisterMusicProviderAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task UnregisterPlayListControllerAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task FocusAudioServiceAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task FocusPlayListControllerAsync(Type serviceType, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task ChangeSongContainerAsync(ContainerBase? container, CancellationToken ctk = default) => Task.CompletedTask;

        public override Task InsertSongAsync(SingleSongBase item, int index = -1, CancellationToken ctk = default)
        {
            if (index < 0) Queue.Add(item); else Queue.Insert(index, item);
            return Task.CompletedTask;
        }

        public override Task InsertSongRangeAsync(List<SingleSongBase> items, int index = -1, CancellationToken ctk = default)
        {
            if (index < 0) Queue.AddRange(items); else Queue.InsertRange(index, items);
            return Task.CompletedTask;
        }

        public override Task RemoveSongAsync(SingleSongBase item, CancellationToken ctk = default)
        {
            Queue.Remove(item);
            return Task.CompletedTask;
        }

        public override Task RemoveSongRangeAsync(List<SingleSongBase> item, CancellationToken ctk = default)
        {
            foreach (var song in item) Queue.Remove(song);
            return Task.CompletedTask;
        }

        public override Task RemoveAllSongAsync(CancellationToken ctk = default)
        {
            ClearQueue();
            return Task.CompletedTask;
        }

        public override Task SetRandomAsync(bool isRandom, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task ReRandomAsync(CancellationToken ctk = default) => Task.CompletedTask;
        public override Task SetPlayModeAsync(string playModeId, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task<List<SingleSongBase>> GetPlaylistAsync(CancellationToken ctk = default) => Task.FromResult(Queue.ToList());
        public override Task<List<SingleSongBase>> GetOrderedPlaylistAsync(CancellationToken ctk = default) => Task.FromResult(Queue.ToList());
        public override Task<int> GetCurrentIndexAsync(CancellationToken ctk = default) => Task.FromResult(CurrentIndex);
        public override Task<SingleSongBase?> GetSongAtAsync(int index, CancellationToken ctk = default) =>
            Task.FromResult(index >= 0 && index < Queue.Count ? Queue[index] : null);
        public override Task ReversePlaylistAsync(CancellationToken ctk = default) => Task.CompletedTask;
        public override Task SeekAsync(long position, CancellationToken ctk = default) => Task.CompletedTask;
        public override Task PlayAsync(CancellationToken ctk = default) => Task.CompletedTask;
        public override Task PauseAsync(CancellationToken ctk = default) => Task.CompletedTask;
        public override Task StopAsync(CancellationToken ctk = default) => Task.CompletedTask;
        public override Task<PreparedPlaybackTicket?> PreparePlaybackAsync(SingleSongBase song, CancellationToken ctk = default) =>
            Task.FromResult<PreparedPlaybackTicket?>(null);
        public override Task<PreparedPlaybackPromotion?> PromotePreparedPlaybackAsync(
            PreparedPlaybackTicket preparedTicket,
            CancellationToken ctk = default) => Task.FromResult<PreparedPlaybackPromotion?>(null);

        public override Task MovePointerToAsync(SingleSongBase song, CancellationToken ctk = default)
        {
            var index = Queue.FindIndex(candidate => candidate.ActualId == song.ActualId);
            if (index >= 0)
            {
                CurrentIndex = index;
                CurrentSong = Queue[index];
            }
            return Task.CompletedTask;
        }

        public override Task MovePointerToIndexAsync(int index, CancellationToken ctk = default)
        {
            if (index >= 0 && index < Queue.Count)
            {
                CurrentIndex = index;
                CurrentSong = Queue[index];
            }
            return Task.CompletedTask;
        }

        public override Task MoveNextAsync(CancellationToken ctk = default) =>
            MovePointerToIndexAsync((CurrentIndex + 1) % Queue.Count, ctk);
        public override Task MovePreviousAsync(CancellationToken ctk = default) =>
            MovePointerToIndexAsync((CurrentIndex - 1 + Queue.Count) % Queue.Count, ctk);

        private sealed class FakePlayListManager(FakePlayCore owner) : PlayListManagerBase
        {
            public override Task AddSongContainerAsync(ContainerBase container, CancellationToken ctk = default) => Task.CompletedTask;
            public override Task RemoveSongContainerAsync(ContainerBase container, CancellationToken ctk = default) => Task.CompletedTask;
            public override Task<List<ContainerBase>> GetAllSongContainersAsync(CancellationToken ctk = default) => Task.FromResult(new List<ContainerBase>());
            public override Task ClearSongContainersAsync(CancellationToken ctk = default) => Task.CompletedTask;
            public override Task<List<SingleSongBase>> GetPlayListAsync(CancellationToken ctk = default) => Task.FromResult(owner.Queue.ToList());
            public override Task AddSongAsync(SingleSongBase song, int index = -1, CancellationToken ctk = default) => owner.InsertSongAsync(song, index, ctk);
            public override Task AddSongRangeAsync(List<SingleSongBase> song, int index = -1, CancellationToken ctk = default) => owner.InsertSongRangeAsync(song, index, ctk);
            public override Task RemoveSongAsync(SingleSongBase song, CancellationToken ctk = default) => owner.RemoveSongAsync(song, ctk);
            public override Task RemoveSongRangeAsync(List<SingleSongBase> song, CancellationToken ctk = default) => owner.RemoveSongRangeAsync(song, ctk);
            public override Task ClearSongsAsync(CancellationToken ctk = default) => owner.RemoveAllSongAsync(ctk);
            public override Task SetSongListAsync(List<SingleSongBase> song, CancellationToken ctk = default)
            {
                owner.Queue = song.ToList();
                return Task.CompletedTask;
            }
        }
    }
}
