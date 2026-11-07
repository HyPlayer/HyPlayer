using HyPlayer.Platform.Runtime;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class IdleResourceReleaseTests
{
    [Test]
    public void CancellationCallbacks_ShouldAllowConcurrentCompletionWithoutHoldingOwnerLock()
    {
        var owner = new HyPlayer.Platform.Runtime.Background.OwnedCancellation();
        using var registration = owner.Token.Register(() =>
        {
            // Completing on another thread must not wait for Cancel to release a lock.
            if (!Task.Run(owner.Complete).Wait(TimeSpan.FromSeconds(3)))
                throw new InvalidOperationException("Cancellation held the ownership lock.");
            // The CTS must remain valid until this callback returns.
            _ = owner.Token;
        });
        owner.Cancel();
        owner.Cancel(); owner.Complete();
    }
    [Test]
    public async Task CompletedLyricLoad_ShouldAllowLateCancellationWithoutLeakingOwnership()
    {
        var load = new HyPlayer.Features.Lyrics.Services.LyricLoad(
            new HyPlayer.Platform.Playback.LocalProvider.LocalSong(), (_, _) => Task.CompletedTask);
        await load.Completion;
        if (!load.Reusable) throw new InvalidOperationException("Completed load unexpectedly unusable.");
        load.Cancel(); load.Cancel();
        if (load.Reusable) throw new InvalidOperationException("Canceled load remained reusable.");
    }
    [Test]
    public async Task ConcurrentRelease_ShouldCoalesceAndDrain()
    {
        using var finish = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var release = new IdleResourceRelease(() =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            finish.Wait(TimeSpan.FromSeconds(5));
        });
        var first = release.RunAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var i = 0; i < 100; i++)
                if (!ReferenceEquals(first, release.RunAsync())) throw new InvalidOperationException("Release was not coalesced.");
            if (release.DrainAsync().IsCompleted) throw new InvalidOperationException("Drain completed before release.");
        }
        finally { finish.Set(); }
        await release.DrainAsync();
        if (calls != 1) throw new InvalidOperationException("Overlapping cleanup ran more than once.");
        await release.RunAsync();
        if (calls != 2) throw new InvalidOperationException("A later cleanup was not allowed.");
    }

    [Test]
    public async Task FailedRelease_ShouldSurfaceAndAllowRetry()
    {
        var calls = 0;
        var release = new IdleResourceRelease(() =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new IOException("native cleanup failed");
        });
        try
        {
            await release.RunAsync();
            throw new InvalidOperationException("Cleanup failure was hidden.");
        }
        catch (IOException) { }
        await release.RunAsync();
        await release.DrainAsync();
        if (calls != 2) throw new InvalidOperationException("Cleanup did not recover after failure.");
    }
}
