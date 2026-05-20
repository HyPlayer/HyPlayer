using HyPlayer.Services.Abstractions;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;

namespace HyPlayer.Services.Background;

/// <inheritdoc />
public sealed class BackgroundTaskRunner : IBackgroundTaskRunner
{
    public void Forget(Task task, string operationName)
    {
        _ = ObserveAsync(task, operationName);
    }

    public void Forget(IAsyncAction action, string operationName)
    {
        if (action == null) return;
        Forget(action.AsTask(), operationName);
    }

    public void Forget(Func<Task> taskFactory, string operationName)
    {
        try
        {
            Forget(taskFactory(), operationName);
        }
        catch (Exception ex)
        {
            Log(operationName, ex);
        }
    }

    private static async Task ObserveAsync(Task task, string operationName)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected for playback/load cancellation paths.
        }
        catch (Exception ex)
        {
            Log(operationName, ex);
        }
    }

    private static void Log(string operationName, Exception ex)
    {
        Debug.WriteLine($"Background task '{operationName}' failed: {ex}");
    }
}
