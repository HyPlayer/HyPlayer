using HyPlayer.Platform.Playback.MediaSources;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class DeferredStreamCleanupTests
{
    private sealed class Resource(Action? dispose = null) : IDisposable
    {
        public int Disposals;
        public void Dispose() { Disposals++; dispose?.Invoke(); }
    }

    [Test]
    public async Task CreationInProgress_ShouldKeepInputAliveAndTransferExactlyOnce()
    {
        var input = new Resource();
        var creating = new TaskCompletionSource<Resource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = DeferredStreamCleanup.ReleaseAsync(Task.FromResult(input), Task.CompletedTask, creating.Task);
        if (input.Disposals != 0 || cleanup.IsCompleted) throw new InvalidOperationException("Input retired before decoder settled.");
        var output = new Resource(input.Dispose);
        creating.SetResult(output);
        await cleanup;
        if (input.Disposals != 1 || output.Disposals != 1) throw new InvalidOperationException("Ownership was duplicated or leaked.");
    }

    [Test]
    public async Task FailedProbeAndCreation_ShouldBeObservedBeforeInputDisposal()
    {
        var input = new Resource();
        var probing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = DeferredStreamCleanup.ReleaseAsync(Task.FromResult(input), probing.Task, Task.FromException<Resource?>(new IOException()));
        if (input.Disposals != 0) throw new InvalidOperationException("Active read was disposed.");
        probing.SetException(new IOException());
        await cleanup;
        if (input.Disposals != 1) throw new InvalidOperationException("Failed creation leaked its input.");
    }
}
