using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Transitions;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class TrackTransitionTests
{
    [Test]
    public async Task Direct_advances_only_after_completion()
    {
        var host = new FakeHost();
        var transition = new DirectTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        Ensure(host.AdvanceCount == 0, "Direct must not advance from a position tick.");

        await transition.OnTrackCompletedAsync(context, CancellationToken.None);
        Ensure(host.AdvanceCount == 1, "Direct must advance exactly once on completion.");
    }

    [Test]
    public async Task Gapless_short_track_falls_back_without_preloading()
    {
        var host = new FakeHost();
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(28));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        Ensure(host.PrepareCount == 0, "A short track must not be preloaded.");

        await transition.OnTrackCompletedAsync(context, CancellationToken.None);
        Ensure(host.AdvanceCount == 1, "A short track must use direct completion.");
    }

    [Test]
    public async Task Gapless_promotes_prepared_ticket_and_releases_outgoing()
    {
        var host = new FakeHost();
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await transition.OnTrackCompletedAsync(context, CancellationToken.None);

        Ensure(host.PrepareCount == 1, "Gapless must prepare once.");
        Ensure(host.PromoteCount == 1, "Gapless must promote once.");
        Ensure(host.Incoming.PlayCount == 1, "The promoted incoming ticket must start.");
        Ensure(host.Outgoing.DisposeCount == 1, "The old ticket lease must be released.");
        Ensure(host.AdvanceCount == 0, "A successful promotion must not also direct-advance.");
    }

    [Test]
    public async Task Crossfade_uses_equal_power_endpoints_and_releases_outgoing()
    {
        var host = new FakeHost();
        var transition = new CrossFadeTransition();
        var context = CreateContext(
            host,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromMilliseconds(179_990),
            TimeSpan.FromMilliseconds(30));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        Ensure(host.PromoteCount == 1, "Crossfade must promote when entering its fade window.");
        Ensure(host.Outgoing.LastVolume > 0.9, "Equal-power fade starts with the outgoing track audible.");
        Ensure(host.Incoming.LastVolume < 0.1, "Equal-power fade starts with the incoming track near zero.");

        await Task.Delay(50);
        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        Ensure(host.Incoming.LastVolume > 0.99, "Equal-power fade ends at the incoming target.");
        Ensure(host.Outgoing.DisposeCount == 1, "Crossfade must release the outgoing lease.");
    }

    [Test]
    public async Task Cancel_releases_an_unadopted_preload()
    {
        var host = new FakeHost();
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await transition.CancelAsync(CancellationToken.None);

        Ensure(host.Incoming.DisposeCount == 1, "Cancellation must release a prepared ticket.");
        Ensure(host.PromoteCount == 0, "Cancellation must not promote a prepared ticket.");
    }

    [Test]
    public async Task Promotion_validation_failure_releases_ticket_and_uses_direct_completion()
    {
        var host = new FakeHost { PromoteSucceeds = false };
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await transition.OnTrackCompletedAsync(context, CancellationToken.None);

        Ensure(host.Incoming.DisposeCount == 1, "A rejected promotion must release its ticket.");
        Ensure(host.AdvanceCount == 1, "A rejected promotion must fall back to direct completion.");
    }

    [Test]
    public async Task Active_ab_loop_cancels_existing_preload()
    {
        var host = new FakeHost();
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        var abContext = CreateContext(
            host,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(156),
            hasActiveAbLoop: true);
        await transition.OnPositionChangedAsync(abContext, CancellationToken.None);

        Ensure(host.Incoming.DisposeCount == 1, "Enabling AB repeat must release a preload.");
        Ensure(host.PromoteCount == 0, "AB repeat must not promote a preload.");
    }

    [Test]
    public async Task Gapless_play_failure_releases_prepared_and_uses_direct_completion()
    {
        var host = new FakeHost();
        host.Incoming.PlayFailuresRemaining = 1;
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await transition.OnTrackCompletedAsync(context, CancellationToken.None);

        Ensure(host.PromoteCount == 0, "A prepared ticket that cannot start must not be promoted.");
        Ensure(host.Incoming.DisposeCount == 1, "A prepared ticket that cannot start must be released.");
        Ensure(host.AdvanceCount == 1, "A prepared start failure must use direct completion.");
    }

    [Test]
    public async Task Gapless_outgoing_release_failure_is_retained_for_the_next_tick()
    {
        var host = new FakeHost();
        host.Outgoing.DisposeFailuresRemaining = 1;
        var transition = new GaplessTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await ExpectFailureAsync(() => transition.OnTrackCompletedAsync(context, CancellationToken.None));

        Ensure(host.Outgoing.DisposeAttempts == 1, "The first outgoing release must be attempted.");
        Ensure(host.Outgoing.DisposeCount == 0, "A failed release must not be marked complete.");

        await transition.OnPositionChangedAsync(context, CancellationToken.None);

        Ensure(host.Outgoing.DisposeAttempts == 2, "The retained promotion must retry outgoing release.");
        Ensure(host.Outgoing.DisposeCount == 1, "The retry must settle the outgoing ticket.");
        Ensure(host.AdvanceCount == 0, "A promoted incoming track must not be advanced again.");
    }

    [Test]
    public async Task Crossfade_incoming_volume_failure_still_settles_outgoing()
    {
        var host = new FakeHost();
        host.Incoming.FailVolumeOnCall = 2;
        var transition = new CrossFadeTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await ExpectFailureAsync(() => transition.OnTrackCompletedAsync(context, CancellationToken.None));

        Ensure(host.Outgoing.LastVolume == 0, "A failed incoming normalization must still mute the outgoing ticket.");
        Ensure(host.Outgoing.PauseCount == 1, "A failed incoming normalization must still pause the outgoing ticket.");
        Ensure(host.Outgoing.DisposeCount == 1, "A failed incoming normalization must still release the outgoing ticket.");
    }

    [Test]
    public async Task Crossfade_outgoing_release_failure_is_retained_until_cancel()
    {
        var host = new FakeHost();
        host.Outgoing.DisposeFailuresRemaining = 1;
        var transition = new CrossFadeTransition();
        var context = CreateContext(host, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(155));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        await ExpectFailureAsync(() => transition.OnTrackCompletedAsync(context, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await transition.CancelAsync(cancellation.Token);

        Ensure(host.Outgoing.DisposeAttempts == 2, "Cancellation must retry a retained outgoing lease.");
        Ensure(host.Outgoing.DisposeCount == 1, "The retained outgoing lease must eventually be released.");
        Ensure(!host.Outgoing.LastPauseToken.IsCancellationRequested, "Outgoing settlement must not inherit caller cancellation.");
    }

    [Test]
    public async Task Crossfade_cancel_with_cancelled_token_hard_cuts_the_outgoing_ticket()
    {
        var host = new FakeHost();
        var transition = new CrossFadeTransition();
        var context = CreateContext(
            host,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(179),
            TimeSpan.FromSeconds(5));

        await transition.OnPositionChangedAsync(context, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await transition.CancelAsync(cancellation.Token);

        Ensure(host.PromoteCount == 1, "The test must enter an active crossfade.");
        Ensure(host.Outgoing.LastVolume == 0, "Cancellation must mute the outgoing ticket.");
        Ensure(host.Outgoing.PauseCount == 1, "Cancellation must pause the outgoing ticket.");
        Ensure(host.Outgoing.DisposeCount == 1, "Cancellation must release the outgoing ticket.");
    }

    [Test]
    public async Task Chopin_adapter_disposal_ignores_cancellation_and_is_idempotent()
    {
        var player = new DisposalTrackingPlayer();
        var source = new DisposablePlaybackSource();
        var service = new ChopinAudioService(player, new Setting());
        var ticket = new ChopinAudioTicket
        {
            AudioServiceId = service.Id,
            MusicResource = new FakeMusicResource(),
            PlaybackSource = source,
            Status = AudioTicketStatus.Playing
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await service.DisposeAudioTicketAsync(ticket, cancellation.Token);
        await service.DisposeAudioTicketAsync(ticket, cancellation.Token);

        Ensure(player.PauseCount == 1, "Disposal must stop the playback source once.");
        Ensure(player.DisconnectCount == 1, "Disposal must disconnect the playback source once.");
        Ensure(source.DisposeCount == 1, "Disposal must release the source once.");
        Ensure(ticket.Status == AudioTicketStatus.Stopped, "Disposal must mark the ticket stopped.");
    }

    [Test]
    public async Task Chopin_adapter_pausing_outgoing_ticket_does_not_pause_promoted_source()
    {
        var player = new DisposalTrackingPlayer();
        var promotedSource = new DisposablePlaybackSource();
        var outgoingSource = new DisposablePlaybackSource();
        player.PrimarySource = promotedSource;
        var service = new ChopinAudioService(player, new Setting());
        var outgoingTicket = new ChopinAudioTicket
        {
            AudioServiceId = service.Id,
            MusicResource = new FakeMusicResource(),
            PlaybackSource = outgoingSource,
            Status = AudioTicketStatus.Playing
        };

        await service.PauseAudioTicketAsync(outgoingTicket);

        Ensure(player.PauseCount == 1, "The outgoing source must be paused.");
        Ensure(player.PauseAllCount == 0, "Pausing an outgoing source must not pause the promoted source.");
        Ensure(outgoingTicket.Status == AudioTicketStatus.Paused, "The outgoing ticket must be marked paused.");
    }

    [Test]
    public async Task Chopin_adapter_pausing_primary_ticket_still_pauses_graph()
    {
        var player = new DisposalTrackingPlayer();
        var primarySource = new DisposablePlaybackSource();
        player.PrimarySource = primarySource;
        var service = new ChopinAudioService(player, new Setting());
        var primaryTicket = new ChopinAudioTicket
        {
            AudioServiceId = service.Id,
            MusicResource = new FakeMusicResource(),
            PlaybackSource = primarySource,
            Status = AudioTicketStatus.Playing
        };

        await service.PauseAudioTicketAsync(primaryTicket);

        Ensure(player.PauseCount == 1, "The primary source must be paused.");
        Ensure(player.PauseAllCount == 1, "Pausing the primary source must pause the graph.");
        Ensure(primaryTicket.Status == AudioTicketStatus.Paused, "The primary ticket must be marked paused.");
    }

    private static TrackTransitionContext CreateContext(
        FakeHost host,
        TimeSpan duration,
        TimeSpan position,
        TimeSpan? crossFade = null,
        bool hasActiveAbLoop = false) =>
        new()
        {
            Host = host,
            Source = host.Source,
            Generation = 7,
            Position = position,
            Duration = duration,
            CanPreload = !hasActiveAbLoop && duration >= TimeSpan.FromSeconds(30),
            HasActiveAbLoop = hasActiveAbLoop,
            PlaybackRate = 1.25,
            CrossFadeDuration = crossFade ?? TimeSpan.FromSeconds(3)
        };

    private static async Task ExpectFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            return;
        }

        throw new InvalidOperationException("The operation was expected to fail.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeHost : ITrackTransitionHost
    {
        public FakeSource Source { get; } = new();
        public FakeTicket Incoming { get; } = new(new FakeSong("next"));
        public FakeTicket Outgoing { get; } = new(new FakeSong("current"));
        public bool PromoteSucceeds { get; init; } = true;
        public int PrepareCount { get; private set; }
        public int PromoteCount { get; private set; }
        public int AdvanceCount { get; private set; }

        public Task<TransitionPreparedTrack?> PrepareNextAsync(
            TrackTransitionContext context,
            CancellationToken ct)
        {
            PrepareCount++;
            return Task.FromResult<TransitionPreparedTrack?>(new TransitionPreparedTrack
            {
                Song = Incoming.Song,
                Ticket = Incoming,
                Generation = context.Generation,
                Source = context.Source,
                CurrentIndex = 0,
                QueueRevision = 0
            });
        }

        public Task<PreparedPlaybackPromotion?> PromoteAsync(
            TransitionPreparedTrack prepared,
            CancellationToken ct)
        {
            PromoteCount++;
            if (!PromoteSucceeds)
                return Task.FromResult<PreparedPlaybackPromotion?>(null);

            return Task.FromResult<PreparedPlaybackPromotion?>(
                new PreparedPlaybackPromotion(Incoming, Outgoing));
        }

        public Task AdvanceDirectAsync(TrackTransitionContext context, CancellationToken ct)
        {
            AdvanceCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTicket : PreparedPlaybackTicket
    {
        public FakeTicket(SingleSongBase song)
        {
            Song = song;
        }

        public override SingleSongBase Song { get; }
        public override double TargetVolume => 1d;
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public int VolumeCallCount { get; private set; }
        public int DisposeAttempts { get; private set; }
        public int DisposeCount { get; private set; }
        public int PlayFailuresRemaining { get; set; }
        public int DisposeFailuresRemaining { get; set; }
        public int FailVolumeOnCall { get; set; } = -1;
        public double LastVolume { get; private set; } = 1d;
        public double LastPlaybackRate { get; private set; } = 1d;
        public CancellationToken LastPauseToken { get; private set; }

        public override Task PlayAsync(CancellationToken ctk = default)
        {
            PlayCount++;
            if (PlayFailuresRemaining > 0)
            {
                PlayFailuresRemaining--;
                throw new InvalidOperationException("Injected play failure.");
            }

            return Task.CompletedTask;
        }

        public override Task PauseAsync(CancellationToken ctk = default)
        {
            PauseCount++;
            LastPauseToken = ctk;
            return Task.CompletedTask;
        }

        public override Task SetVolumeAsync(double volume, CancellationToken ctk = default)
        {
            VolumeCallCount++;
            if (VolumeCallCount == FailVolumeOnCall)
                throw new InvalidOperationException("Injected volume failure.");

            LastVolume = volume;
            return Task.CompletedTask;
        }

        public override Task SetPlaybackRateAsync(double playbackRate, CancellationToken ctk = default)
        {
            LastPlaybackRate = playbackRate;
            return Task.CompletedTask;
        }

        public override Task DisposeAsync()
        {
            DisposeAttempts++;
            if (DisposeFailuresRemaining > 0)
            {
                DisposeFailuresRemaining--;
                throw new InvalidOperationException("Injected dispose failure.");
            }

            DisposeCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DisposalTrackingPlayer : IPlayer
    {
        public int PauseCount { get; private set; }
        public int PauseAllCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public IPlaybackSource PrimarySource { get; set; } = null!;
        public double Volume => 1d;
        public ISMTCManager SMTCManager { get; set; } = null!;
        public int ConnectedPlaybackSourceCount => 0;
        public PlaybackStatus GlobalPlaybackStatus => PlaybackStatus.Paused;
        public IPlaybackSource PrimaryPlaybackSource => PrimarySource;
        public Task InitializePlayer(IAudioSettings settings) => Task.CompletedTask;
        public Task ConnectPlaybackSourceAsync(IPlaybackSource playbackSource, PlaybackOptions options) => Task.CompletedTask;
        public void DisconnectPlaybackSource(IPlaybackSource playbackSource) => DisconnectCount++;
        public void RemoveAllPlaybackSource() { }
        public void PlayAll() { }
        public void PauseAll() => PauseAllCount++;
        public void SeekPlaybackSource(TimeSpan target, IPlaybackSource playbackSource) { }
        public void PausePlaybackSource(IPlaybackSource playbackSource) => PauseCount++;
        public void PlayPlaybackSource(IPlaybackSource playbackSource) { }
        public void SetPlaybackSourceSpeed(double speed, IPlaybackSource playbackSource) { }
        public double GetPlaybackSourceSpeed(IPlaybackSource playbackSource) => 1d;
        public void SetPrimaryPlaybackSource(IPlaybackSource playbackSource) { }
        public void SetOutputVolume(double volume) { }
        public void SetPlaybackSourceOutputVolume(double volume, IPlaybackSource playbackSource) { }
        public Task ChangePlayerServiceImplementation(IAudioSettings settings) => Task.CompletedTask;
    }

    private sealed class DisposablePlaybackSource : IPlaybackSource, IDisposable
    {
        public int DisposeCount { get; private set; }
        public string Name { get; set; } = "disposable";
        public PlaybackSourceType PlaybackSourceType => PlaybackSourceType.Local;
        public Uri Path { get; set; } = new("file:///disposable.mp3");
        public PlaybackStatus PlaybackStatus => PlaybackStatus.Paused;
        public Task CreatePlaybackSource() => Task.CompletedTask;
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeMusicResource : MusicResourceBase
    {
        public override Task<ResourceResultBase> GetResourceAsync(
            ResourceQualityTag? qualityTag = null,
            CancellationToken ctk = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSource : IPlaybackSource
    {
        public string Name { get; set; } = "fake";
        public PlaybackSourceType PlaybackSourceType => PlaybackSourceType.Local;
        public Uri Path { get; set; } = new("file:///fake.mp3");
        public PlaybackStatus PlaybackStatus => PlaybackStatus.Paused;
        public Task CreatePlaybackSource() => Task.CompletedTask;
    }

    private sealed class FakeSong : SingleSongBase
    {
        public FakeSong(string id)
        {
            ActualId = id;
            Name = id;
        }

        public override string ProviderId => "test";
        public override string TypeId => "song";
        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) =>
            Task.FromResult<List<PersonBase>?>([]);
    }
}
