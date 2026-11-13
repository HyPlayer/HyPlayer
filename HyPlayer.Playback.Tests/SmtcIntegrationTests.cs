using HyPlayer.Features.Playback.Services;
using TUnit.Core;
using Windows.Media;

namespace HyPlayer.Playback.Tests;

public sealed class SmtcIntegrationTests
{
    [Test]
    public async Task Low_precision_timeline_sync_runs_every_five_clock_ticks()
    {
        var calls = 0;
        var latestPosition = 0;
        var publishedPosition = 0;
        var synchronizer = new SmtcTimelineSynchronizer(() => true, () =>
        {
            calls++;
            publishedPosition = latestPosition;
        });

        for (var second = 1; second <= 10; second++)
        {
            latestPosition = second;
            await synchronizer.OnSecondTickAsync();
            Ensure(calls == second / 5, "SMTC must only synchronize on every fifth global clock tick.");
            Ensure(publishedPosition == second / 5 * 5, "SMTC must synchronize the latest position.");
        }
    }

    [Test]
    public async Task Disabled_timeline_sync_resets_the_clock_interval()
    {
        var enabled = true;
        var calls = 0;
        var synchronizer = new SmtcTimelineSynchronizer(() => enabled, () => calls++);
        for (var second = 0; second < 4; second++)
            await synchronizer.OnSecondTickAsync();

        enabled = false;
        for (var second = 0; second < 10; second++)
            await synchronizer.OnSecondTickAsync();
        Ensure(calls == 0, "Disabled or paused low-precision sync must not publish a timeline.");

        enabled = true;
        for (var second = 0; second < 4; second++)
            await synchronizer.OnSecondTickAsync();
        Ensure(calls == 0, "Re-enabling sync must start a new five-second interval.");
        await synchronizer.OnSecondTickAsync();
        Ensure(calls == 1, "Re-enabled sync must publish after five seconds.");
    }

    [Test]
    public async Task Slow_timeline_sync_does_not_queue_more_system_calls()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var synchronizer = new SmtcTimelineSynchronizer(() => true, () =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            Ensure(release.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting to release the simulated system call.");
        });
        for (var second = 0; second < 4; second++)
            await synchronizer.OnSecondTickAsync();
        var pending = synchronizer.OnSecondTickAsync();
        try
        {
            Ensure(entered.Wait(TimeSpan.FromSeconds(3)), "The simulated system call did not start.");
            for (var second = 0; second < 20; second++)
                await synchronizer.OnSecondTickAsync().WaitAsync(TimeSpan.FromSeconds(1));
            Ensure(calls == 1, "A blocked SMTC update must not queue additional system calls.");
        }
        finally
        {
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
        }

        for (var second = 0; second < 5; second++)
            await synchronizer.OnSecondTickAsync();
        Ensure(calls == 2, "The next interval must synchronize after the blocked update completes.");
    }

    [Test]
    public async Task Failed_timeline_sync_allows_the_next_interval_to_retry()
    {
        var calls = 0;
        var synchronizer = new SmtcTimelineSynchronizer(() => true, () =>
        {
            if (++calls == 1)
                throw new InvalidOperationException("Simulated SMTC failure.");
        });
        for (var second = 0; second < 4; second++)
            await synchronizer.OnSecondTickAsync();
        try
        {
            await synchronizer.OnSecondTickAsync();
            throw new Exception("The simulated failure was not reported to the caller.");
        }
        catch (InvalidOperationException)
        {
        }

        for (var second = 0; second < 5; second++)
            await synchronizer.OnSecondTickAsync();
        Ensure(calls == 2, "A failed update must release the in-flight guard.");
    }

    [Test]
    public async Task Next_dispatches_only_move_next()
    {
        var calls = new CommandCalls();
        var dispatcher = CreateDispatcher(calls);

        await dispatcher.DispatchAsync(SystemMediaTransportControlsButton.Next);

        Ensure(calls.Next == 1, "SMTC Next must dispatch the next-track command exactly once.");
        Ensure(calls.Play == 0 && calls.Pause == 0 && calls.Previous == 0,
            "SMTC Next must not dispatch any other playback command.");
    }

    [Test]
    public async Task Previous_dispatches_only_move_previous()
    {
        var calls = new CommandCalls();
        var dispatcher = CreateDispatcher(calls);

        await dispatcher.DispatchAsync(SystemMediaTransportControlsButton.Previous);

        Ensure(calls.Previous == 1, "SMTC Previous must dispatch the previous-track command exactly once.");
        Ensure(calls.Play == 0 && calls.Pause == 0 && calls.Next == 0,
            "SMTC Previous must not dispatch any other playback command.");
    }

    [Test]
    public async Task Play_and_pause_dispatch_their_matching_commands()
    {
        var calls = new CommandCalls();
        var dispatcher = CreateDispatcher(calls);

        await dispatcher.DispatchAsync(SystemMediaTransportControlsButton.Play);
        await dispatcher.DispatchAsync(SystemMediaTransportControlsButton.Pause);

        Ensure(calls.Play == 1 && calls.Pause == 1,
            "SMTC Play and Pause must each dispatch their matching command exactly once.");
        Ensure(calls.Next == 0 && calls.Previous == 0,
            "SMTC Play and Pause must not dispatch track navigation commands.");
    }

    [Test]
    public void Track_identity_uppercases_provider_and_preserves_actual_id()
    {
        Ensure(SmtcTrackIdentity.Create("ncm", "14234523") == "NCM-14234523",
            "NCM track identity must use an uppercase provider ID.");
        Ensure(SmtcTrackIdentity.Create("lcl", @"C:\Music\Track-01.flac") == @"LCL-C:\Music\Track-01.flac",
            "Local track identity must preserve the ActualId.");
    }

    [Test]
    public void Track_identity_is_absent_when_a_component_is_missing()
    {
        Ensure(SmtcTrackIdentity.Create(null, "14234523") is null,
            "A missing provider ID must not produce an SMTC track identity.");
        Ensure(SmtcTrackIdentity.Create("ncm", null) is null,
            "A missing ActualId must not produce an SMTC track identity.");
        Ensure(SmtcTrackIdentity.Create(" ", "14234523") is null,
            "A blank provider ID must not produce an SMTC track identity.");
        Ensure(SmtcTrackIdentity.Create("ncm", " ") is null,
            "A blank ActualId must not produce an SMTC track identity.");
    }

    private static SmtcPlaybackCommandDispatcher CreateDispatcher(CommandCalls calls) =>
        new(
            () =>
            {
                calls.Play++;
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Pause++;
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Next++;
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Previous++;
                return Task.CompletedTask;
            });

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CommandCalls
    {
        public int Play { get; set; }
        public int Pause { get; set; }
        public int Next { get; set; }
        public int Previous { get; set; }
    }
}
