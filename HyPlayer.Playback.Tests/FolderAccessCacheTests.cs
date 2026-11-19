using HyPlayer.Platform.Storage;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class FolderAccessCacheTests
{
    [Test]
    public async Task Readers_DoNotStartOrWaitForProbe_AndConcurrentRequestsShareOneProbe()
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new AsyncFolderAccessCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return result.Task;
        }, TimeSpan.FromSeconds(5));
        if (cache.IsUsable("music") || calls != 0) throw new InvalidOperationException("Getter started I/O.");
        var first = cache.ProbeAsync("music");
        var second = cache.ProbeAsync("MUSIC");
        if (!ReferenceEquals(first, second) || calls != 1 || cache.IsUsable("music"))
            throw new InvalidOperationException("Pending readers blocked or probe was duplicated.");
        result.SetResult(true);
        if (!await first || !cache.IsUsable("music")) throw new InvalidOperationException("Probe did not publish result.");
    }

    [Test]
    public async Task ForgottenProbe_CannotOverwriteNewPermissionResult()
    {
        var stale = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new AsyncFolderAccessCache((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? stale.Task : Task.FromResult(false), TimeSpan.FromSeconds(5));
        var first = cache.ProbeAsync("music");
        cache.Forget("music");
        if (await cache.ProbeAsync("music")) throw new InvalidOperationException("Denied access was accepted.");
        stale.SetResult(true);
        if (await first || cache.IsUsable("music")) throw new InvalidOperationException("Stale access overwrote denial.");
    }

    [Test]
    public async Task Timeout_ReturnsFallback_EvenIfProbeIgnoresCancellation()
    {
        var stuck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new AsyncFolderAccessCache((_, _) => stuck.Task, TimeSpan.FromMilliseconds(30));
        var first = cache.ProbeAsync("network");
        if (await first.WaitAsync(TimeSpan.FromSeconds(5)) || cache.IsUsable("network"))
            throw new InvalidOperationException("Timeout did not choose fallback.");
        stuck.SetResult(true);
        if (await cache.ProbeAsync("network") || cache.IsUsable("network"))
            throw new InvalidOperationException("Late completion changed timeout result.");
    }

    [Test]
    public async Task LostAccess_InvalidatesPendingResult_AndCanBeReprobed()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new AsyncFolderAccessCache((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? pending.Task : Task.FromResult(true), TimeSpan.FromSeconds(5));
        var first = cache.ProbeAsync("music");
        cache.MarkUnusable("music");
        pending.SetResult(true);
        if (await first || await cache.ProbeAsync("music")) throw new InvalidOperationException("Lost access resurrected.");
        cache.Forget("music");
        if (!await cache.ProbeAsync("music") || !cache.IsUsable("music"))
            throw new InvalidOperationException("Explicit revalidation failed.");
    }

    [Test]
    public async Task CacheCapacity_EvictsOldPaths()
    {
        var cache = new AsyncFolderAccessCache((_, _) => Task.FromResult(true), TimeSpan.FromSeconds(5), 2);
        await cache.ProbeAsync("one");
        await cache.ProbeAsync("two");
        await cache.ProbeAsync("three");
        if (cache.IsUsable("one") || !cache.IsUsable("two") || !cache.IsUsable("three"))
            throw new InvalidOperationException("Cache is not bounded.");
    }

    [Test]
    public async Task ProbeFailures_AreObservedAndChooseFallback()
    {
        var cache = new AsyncFolderAccessCache((_, _) => Task.FromException<bool>(new IOException("denied")),
            TimeSpan.FromSeconds(5));
        if (await cache.ProbeAsync("music") || cache.IsUsable("music"))
            throw new InvalidOperationException("Failed probe was accepted.");
    }
}
