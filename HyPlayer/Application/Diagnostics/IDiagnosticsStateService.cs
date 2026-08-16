using System.Collections.Generic;
using ObservableCollections;

namespace HyPlayer.Application.Diagnostics;

public interface IDiagnosticsStateService
{
    List<string> ErrorMessages { get; }
    ObservableList<string> Logs { get; }
}
