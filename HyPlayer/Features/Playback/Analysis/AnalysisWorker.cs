using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Analysis;

internal static class AnalysisWorker
{
    public const string ThreadName = "HyPlayer analysis";
    private const int IdleExitMilliseconds = 30000;

    private static readonly object Gate = new();
    private static readonly Queue<Action> Pending = new();
    private static Thread? _thread;
    private static long _completed;
    private static int _running;

    public static long CompletedCount => Interlocked.Read(ref _completed);

    public static bool IsBusy
    {
        get
        {
            if (Volatile.Read(ref _running) != 0) return true;
            lock (Gate) return Pending.Count > 0;
        }
    }

    public static Task<T> Run<T>(Func<T> work, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            Pending.Enqueue(() => Execute(work, completion, ct));
            if (_thread is null)
            {
                _thread = new Thread(Loop)
                {
                    IsBackground = true,
                    Name = ThreadName,
                    Priority = ThreadPriority.BelowNormal
                };
                _thread.Start();
            }
            else
            {
                Monitor.Pulse(Gate);
            }
        }
        return completion.Task;
    }

    private static void Execute<T>(Func<T> work, TaskCompletionSource<T> completion, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested) completion.TrySetCanceled(ct);
            else completion.TrySetResult(work());
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static void Loop()
    {
        while (true)
        {
            Action work;
            lock (Gate)
            {
                while (Pending.Count == 0)
                {
                    if (Monitor.Wait(Gate, IdleExitMilliseconds) || Pending.Count > 0) continue;
                    _thread = null;
                    return;
                }
                work = Pending.Dequeue();
                Volatile.Write(ref _running, 1);
            }
            work();
            Interlocked.Increment(ref _completed);
            Volatile.Write(ref _running, 0);
        }
    }
}
