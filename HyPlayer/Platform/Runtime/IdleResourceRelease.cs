using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Runtime;

/// <summary>Coalesces synchronous native-resource release and exposes its completion.</summary>
internal sealed class IdleResourceRelease(Action release)
{
    private readonly Lock _gate = new();
    private Task _pending = Task.CompletedTask;

    public Task RunAsync()
    {
        lock (_gate)
        {
            if (!_pending.IsCompleted) return _pending;
            // Synchronous native disposal must not block UI. This is not an I/O wrapper.
            return _pending = Task.Run(release);
        }
    }

    public Task DrainAsync()
    {
        lock (_gate) return _pending;
    }
}
