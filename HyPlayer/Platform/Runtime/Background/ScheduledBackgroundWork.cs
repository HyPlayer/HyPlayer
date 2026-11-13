#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Runtime.Background;

/// <summary>Owns one coalesced, cancellable background operation and observes its failures.</summary>
public sealed class ScheduledBackgroundWork
{
    private readonly object _sync = new();
    private readonly IBackgroundTaskRunner _runner;
    private WorkCancellation? _cancellation;
    private Task _work = Task.CompletedTask;
    private bool _stopped;

    /// <summary>Creates an owner using the shared error boundary, or a supplied observer.</summary>
    public ScheduledBackgroundWork(IBackgroundTaskRunner? runner = null)
    {
        _runner = runner ?? new BackgroundTaskRunner();
    }

    /// <summary>Schedules work if the owner is active and no previous operation is running.</summary>
    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> action, string operationName)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        lock (_sync)
        {
            if (_stopped || !_work.IsCompleted) return;
            var cancellation = new WorkCancellation();
            _cancellation = cancellation;
            _work = RunAsync(delay, action, cancellation);
            _runner.Forget(_work, operationName);
        }
    }

    private async Task RunAsync(TimeSpan delay, Func<CancellationToken, Task> action,
        WorkCancellation cancellation)
    {
        // Never invoke an arbitrary work factory while Schedule owns the state lock.
        await Task.Yield();
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            await action(cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            }
            cancellation.Release();
        }
    }

    /// <summary>Prevents new scheduling, cancels work, and waits for its owned operation to finish.</summary>
    public async Task StopAsync()
    {
        Task work;
        WorkCancellation? cancellation;
        lock (_sync)
        {
            _stopped = true;
            cancellation = _cancellation;
            // The worker owns one reference until its finally. Retain a separate lease
            // while cancelling, so worker completion cannot dispose this source first.
            cancellation?.Retain();
            work = _work;
        }
        try { cancellation?.Cancel(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Background cancellation callback failed: {ex}");
        }
        finally { cancellation?.Release(); }
        try { await work.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { /* The shared background runner already records this failure. */ }
    }

    /// <summary>Allows scheduling again after the previous StopAsync has completed.</summary>
    public void Resume()
    {
        lock (_sync) _stopped = false;
    }

    private sealed class WorkCancellation
    {
        private readonly CancellationTokenSource _source = new();
        private int _references = 1;

        public CancellationToken Token => _source.Token;
        public void Retain() => Interlocked.Increment(ref _references);
        public void Cancel() => _source.Cancel();
        public void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0) _source.Dispose();
        }
    }
}
