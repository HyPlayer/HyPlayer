using HyPlayer.Features.Playback.Services;
using TUnit.Core;
using Windows.Media;

namespace HyPlayer.Playback.Tests;

public sealed class SmtcIntegrationTests
{
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
