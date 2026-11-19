using HyPlayer.Platform.Runtime.Background;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class ScheduledBackgroundWorkTests
{
    [Test]
    public async Task ThrowingCancellationCallback_ShouldStillDrainTheWorker()
    {
        var work = new ScheduledBackgroundWork();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        work.Schedule(TimeSpan.Zero, async token =>
        {
            using var callback = token.Register(() => throw new IOException("cancel callback failed"));
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { finished = true; }
        }, "faulting cancellation callback");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await work.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (!finished) throw new InvalidOperationException("Stop skipped draining after callback failure.");
    }
    [Test]
    public async Task Stop_ShouldInvokeCancellationOutsideStateLockAndRetainSourceUntilItReturns()
    {
        var work = new ScheduledBackgroundWork();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackBlocked = false;
        var tokenStillUsable = false;
        var rejectedCalls = 0;
        work.Schedule(TimeSpan.Zero, async token =>
        {
            // Let this registration outlive the action; the owner's source cleans it up.
            // The callback waits for another Stop, guaranteeing worker finally has finished.
            _ = token.Register(() =>
            {
                // Regression: Cancel under _sync deadlocks this callback against Schedule.
                // A bounded wait makes the old behavior fail instead of hanging the runner.
                var scheduling = Task.Run(async () =>
                {
                    work.Schedule(TimeSpan.Zero, _ =>
                    {
                        Interlocked.Increment(ref rejectedCalls);
                        return Task.CompletedTask;
                    }, "work during stopping");
                    await work.StopAsync();
                });
                callbackBlocked = !scheduling.Wait(TimeSpan.FromSeconds(2));
                using var lateRegistration = token.Register(() => { });
                tokenStillUsable = token.WaitHandle.WaitOne(0);
            });
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, "work with cancellation callback");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await work.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (callbackBlocked) throw new InvalidOperationException("Cancellation held the scheduling lock.");
        if (!tokenStillUsable) throw new InvalidOperationException("Cancellation source lifetime was not retained.");
        if (rejectedCalls != 0) throw new InvalidOperationException("Stopped owner admitted new work.");
    }

    [Test]
    public async Task Schedule_ShouldCoalesceUntilRunningOperationFinishes()
    {
        var work = new ScheduledBackgroundWork();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        work.Schedule(TimeSpan.Zero, async token =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, "test active work");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Parallel.For(0, 64, index => work.Schedule(TimeSpan.Zero, token =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, "duplicate test work"));
        await work.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (calls != 1) throw new InvalidOperationException($"Expected one operation, got {calls}.");
    }

    [Test]
    public async Task Stop_ShouldCancelDelayRejectNewWorkAndAllowExplicitResume()
    {
        var work = new ScheduledBackgroundWork();
        var calls = 0;
        Task Run(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }
        work.Schedule(TimeSpan.FromMinutes(1), Run, "delayed test work");
        await work.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        work.Schedule(TimeSpan.Zero, Run, "stopped test work");
        if (calls != 0) throw new InvalidOperationException("Stopped work ran.");
        work.Resume();
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Schedule(TimeSpan.Zero, _ =>
        {
            Interlocked.Increment(ref calls);
            resumed.SetResult();
            return Task.CompletedTask;
        }, "resumed test work");
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await work.StopAsync();
        if (calls != 1) throw new InvalidOperationException("Resume did not run exactly once.");
    }

    [Test]
    public async Task Failure_ShouldNotPreventShutdownOrLaterScheduling()
    {
        var observer = new RecordingObserver();
        var work = new ScheduledBackgroundWork(observer);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Schedule(TimeSpan.Zero, _ =>
        {
            failed.SetResult();
            throw new InvalidOperationException("Expected test failure.");
        }, "faulted test work");
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await work.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await observer.Failure.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (observer.Operation != "faulted test work") throw new InvalidOperationException("Failure lost its operation context.");
        work.Resume();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Schedule(TimeSpan.Zero, _ =>
        {
            completed.SetResult();
            return Task.CompletedTask;
        }, "recovered test work");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await work.StopAsync();
    }
    private sealed class RecordingObserver : IBackgroundTaskRunner
    {
        public readonly TaskCompletionSource Failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Operation;

        public void Forget(Task task, string operationName) => _ = ObserveAsync(task, operationName);
        public void Forget(Windows.Foundation.IAsyncAction action, string operationName) =>
            Forget(action.AsTask(), operationName);
        public void Forget(Func<Task> factory, string operationName) => Forget(factory(), operationName);

        private async Task ObserveAsync(Task task, string operationName)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException)
            {
                Operation = operationName;
                Failure.TrySetResult();
            }
        }
    }

}
