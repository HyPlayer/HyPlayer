using System.Collections.Generic;
using ObservableCollections;

namespace HyPlayer.Application.Diagnostics;

public sealed class DiagnosticsStateService : IDiagnosticsStateService
{
    public List<string> ErrorMessages { get; } = [];
    public ObservableList<string> Logs { get; } = [];
}
