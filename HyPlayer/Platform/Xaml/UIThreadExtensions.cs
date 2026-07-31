using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace HyPlayer.Platform.Xaml;

/// <summary>
/// Helpers for dispatching work to the UI thread that owns a XAML object.
/// </summary>
public static class UIThreadExtensions
{
    public static Task RunOnUIThreadAsync(this DependencyObject owner, Action action)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.Dispatcher.RunOnUIThreadAsync(action);
    }

    public static Task RunOnUIThreadAsync(this DependencyObject owner, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.Dispatcher.RunOnUIThreadAsync(action);
    }

    internal static Task RunOnUIThreadAsync(this CoreDispatcher dispatcher, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);

        if (dispatcher.HasThreadAccess)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        return dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action()).AsTask();
    }

    internal static Task RunOnUIThreadAsync(this CoreDispatcher dispatcher, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);

        if (dispatcher.HasThreadAccess)
        {
            try
            {
                return action() ?? Task.FromException(
                    new InvalidOperationException("The UI action returned a null task."));
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncAction dispatchOperation;
        try
        {
            dispatchOperation = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Task task;
                try
                {
                    task = action() ?? throw new InvalidOperationException("The UI action returned a null task.");
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                    return;
                }

                _ = CompleteAsync(task, completion);
            });
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }

        _ = ObserveDispatchAsync(dispatchOperation, completion);
        return completion.Task;
    }

    private static async Task CompleteAsync(Task task, TaskCompletionSource<object?> completion)
    {
        try
        {
            await task;
            completion.TrySetResult(null);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static async Task ObserveDispatchAsync(
        IAsyncAction dispatchOperation,
        TaskCompletionSource<object?> completion)
    {
        try
        {
            await dispatchOperation;
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }
}
