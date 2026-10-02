using HyPlayer.Application.Diagnostics;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class DiagnosticsStateServiceTests
{
    [Test]
    public void Error_messages_are_bounded_to_the_recent_entries()
    {
        var diagnostics = new DiagnosticsStateService();

        for (var index = 0; index < 501; index++)
            diagnostics.AddError(index.ToString());

        Check(diagnostics.ErrorMessages.Count == 500, "Error messages must remain bounded.");
        Check(diagnostics.ErrorMessages[0] == "1", "The oldest error should be evicted first.");
        Check(diagnostics.ErrorMessages[^1] == "500", "The newest error should be retained.");
    }

    [Test]
    public void Logs_are_bounded_to_the_recent_entries()
    {
        var diagnostics = new DiagnosticsStateService();

        for (var index = 0; index < 501; index++)
            diagnostics.AddLog(index.ToString());

        Check(diagnostics.Logs.Count == 500, "Logs must remain bounded.");
        Check(diagnostics.Logs[0] == "1", "The oldest log should be evicted first.");
        Check(diagnostics.Logs[^1] == "500", "The newest log should be retained.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
