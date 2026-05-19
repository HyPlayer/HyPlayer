using System.Collections.Generic;
using System.Collections.ObjectModel;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services;

public sealed class DiagnosticsStateService : IDiagnosticsStateService
{
    public List<string> ErrorMessages { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
}
