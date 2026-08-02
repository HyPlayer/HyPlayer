using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HyPlayer.Application.Diagnostics;

public sealed class DiagnosticsStateService : IDiagnosticsStateService
{
    public List<string> ErrorMessages { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
}