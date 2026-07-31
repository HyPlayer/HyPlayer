using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HyPlayer.Application.Diagnostics;

public interface IDiagnosticsStateService
{
    List<string> ErrorMessages { get; }
    ObservableCollection<string> Logs { get; }
}