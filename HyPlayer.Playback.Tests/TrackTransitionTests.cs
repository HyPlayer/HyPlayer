using HyPlayer.Features.Playback.Transitions;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
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

            return Task.FromResult<PreparedPlaybackPromotion?>(new PreparedPlaybackPromotion
            {
                Incoming = Incoming,
                Outgoing = Outgoing
            });
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
        public int DisposeCount { get; private set; }
        public double LastVolume { get; private set; } = 1d;
        public double LastPlaybackRate { get; private set; } = 1d;

        public override Task PlayAsync(CancellationToken ctk = default)
        {
            PlayCount++;
            return Task.CompletedTask;
        }

        public override Task PauseAsync(CancellationToken ctk = default) => Task.CompletedTask;

        public override Task SetVolumeAsync(double volume, CancellationToken ctk = default)
        {
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
            DisposeCount++;
            return Task.CompletedTask;
        }
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
