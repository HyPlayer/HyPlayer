using System.Collections.Generic;
using ObservableCollections;

namespace HyPlayer.Application.Diagnostics;

public sealed class DiagnosticsStateService : IDiagnosticsStateService
{
    private const int MaxEntries = 500;

    public List<string> ErrorMessages { get; } = [];
    public ObservableList<string> Logs { get; } = [];

    public void AddError(string message)
    {
        AddBounded(ErrorMessages, message);
    }

    public void AddLog(string message)
    {
        Logs.Add(message);
        while (Logs.Count > MaxEntries)
            Logs.RemoveAt(0);
    }

    private static void AddBounded(List<string> entries, string message)
    {
        entries.Add(message);
        if (entries.Count > MaxEntries)
            entries.RemoveRange(0, entries.Count - MaxEntries);
    }
}
