using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HyPlayer.Services.Abstractions;

public interface IDiagnosticsStateService
{
    List<string> ErrorMessages { get; }
    ObservableCollection<string> Logs { get; }
}
