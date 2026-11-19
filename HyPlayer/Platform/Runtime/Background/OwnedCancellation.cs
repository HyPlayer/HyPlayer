using System.Threading;

namespace HyPlayer.Platform.Runtime.Background;

/// <summary>Defers source disposal until both its worker and cancellation callbacks finish.</summary>
internal sealed class OwnedCancellation
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _source = new();
    private int _cancelling;
    private bool _complete;

    public CancellationToken Token => _source.Token;

    public void Cancel()
    {
        lock (_gate)
        {
            if (_complete) return;
            _cancelling++;
        }
        try { _source.Cancel(); }
        finally
        {
            lock (_gate)
            {
                if (--_cancelling == 0 && _complete) _source.Dispose();
            }
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_complete) return;
            _complete = true;
            if (_cancelling == 0) _source.Dispose();
        }
    }
}
