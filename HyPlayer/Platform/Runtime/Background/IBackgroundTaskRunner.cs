using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace HyPlayer.Platform.Runtime.Background;

/// <summary>
/// Runs fire-and-forget tasks through a single observable error boundary.
/// </summary>
public interface IBackgroundTaskRunner
{
    /// <summary>
    /// Runs a task without awaiting it at the call site and logs unexpected failures.
    /// </summary>
    void Forget(Task task, string operationName);

    /// <summary>
    /// Runs a WinRT async action without awaiting it at the call site and logs unexpected failures.
    /// </summary>
    void Forget(IAsyncAction action, string operationName);

    /// <summary>
    /// Runs a task factory without awaiting it at the call site and logs unexpected failures.
    /// </summary>
    void Forget(Func<Task> taskFactory, string operationName);
}
