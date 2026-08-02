using System;
using System.Threading.Tasks;

namespace HyPlayer.Application.Threading;

/// <summary>
///     Dispatches work to the application's main UI thread.
/// </summary>
public interface IUIThreadDispatcher
{
    /// <summary>
    ///     Runs synchronous work on the main UI thread when a UI view is available.
    /// </summary>
    Task<bool> TryRunAsync(Action action);

    /// <summary>
    ///     Runs asynchronous work on the main UI thread when a UI view is available.
    /// </summary>
    Task<bool> TryRunAsync(Func<Task> action);
}