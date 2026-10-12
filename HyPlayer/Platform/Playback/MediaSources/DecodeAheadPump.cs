#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace HyPlayer.Platform.Playback.MediaSources;

internal static class DecodeBufferStatistics
{
    public static long Created;
    public static long Returned;
}

internal class DecodedChunk
{
    public nint Data;
    public int Length;
    public TimeSpan Timestamp;
    public TimeSpan Duration;
}

internal interface IChunkDecoder : IDisposable
{
    int ReadChunk(nint destination, out TimeSpan timestamp, out TimeSpan duration);
    void Seek(TimeSpan position);
    void Interrupt();
}

internal sealed class DecodeAheadPump : IDisposable
{
    private const int JoinMilliseconds = 250;
    private const int InterruptedJoinMilliseconds = 2000;

    private readonly object _gate = new();
    private readonly IChunkDecoder _decoder;
    private readonly Func<DecodedChunk> _allocate;
    private readonly int _capacity;
    private readonly int _idleExitMilliseconds;
    private readonly Queue<DecodedChunk> _ready = new();
    private readonly Stack<DecodedChunk> _free = new();
    private readonly Queue<Action<DecodedChunk?>> _waiters = new();
    private Thread? _thread;
    private Action? _seekCompleted;
    private TimeSpan _seekPosition;
    private bool _seekRequested;
    private int _generation;
    private bool _ended;
    private bool _disposed;
    private Exception? _failure;

    public DecodeAheadPump(IChunkDecoder decoder, int capacity, Func<DecodedChunk> allocate, int idleExitMilliseconds = 10000)
    {
        _decoder = decoder;
        _capacity = Math.Max(1, capacity);
        _allocate = allocate;
        _idleExitMilliseconds = idleExitMilliseconds;
    }

    public Exception? Failure
    {
        get
        {
            lock (_gate) return _failure;
        }
    }

    public bool IsDecoding
    {
        get
        {
            lock (_gate) return _thread is { IsAlive: true };
        }
    }

    public bool TryTake(out DecodedChunk? chunk)
    {
        lock (_gate)
        {
            if (_ready.Count > 0)
            {
                chunk = _ready.Dequeue();
                Wake();
                return true;
            }
            chunk = null;
            if (_disposed || _ended) return true;
            Wake();
            return false;
        }
    }

    public void WhenReady(Action<DecodedChunk?> continuation)
    {
        DecodedChunk? chunk = null;
        lock (_gate)
        {
            if (_ready.Count > 0)
            {
                chunk = _ready.Dequeue();
                Wake();
            }
            else if (!_disposed && !_ended)
            {
                _waiters.Enqueue(continuation);
                Wake();
                return;
            }
        }
        continuation(chunk);
    }

    public void Return(DecodedChunk chunk)
    {
        lock (_gate)
        {
            if (!_disposed) _free.Push(chunk);
        }

        Interlocked.Increment(ref DecodeBufferStatistics.Returned);
    }

    public void Seek(TimeSpan position, Action completed)
    {
        Action? superseded;
        lock (_gate)
        {
            if (_disposed)
            {
                superseded = completed;
            }
            else
            {
                superseded = _seekCompleted;
                _seekCompleted = completed;
                _seekPosition = position;
                _seekRequested = true;
                _generation++;
                _ended = false;
                _failure = null;
                while (_ready.Count > 0) _free.Push(_ready.Dequeue());
                Wake();
            }
        }
        superseded?.Invoke();
    }

    public void Dispose()
    {
        Thread? thread;
        Action? seekCompleted;
        Action<DecodedChunk?>[] waiters;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
            seekCompleted = _seekCompleted;
            _seekCompleted = null;
            _seekRequested = false;
            waiters = _waiters.ToArray();
            _waiters.Clear();
            _ready.Clear();
            _free.Clear();
            Monitor.PulseAll(_gate);
        }
        seekCompleted?.Invoke();
        foreach (var waiter in waiters) waiter(null);
        if (thread is null)
        {
            _decoder.Dispose();
            return;
        }
        if (thread == Thread.CurrentThread || thread.Join(JoinMilliseconds)) return;
        _decoder.Interrupt();
        thread.Join(InterruptedJoinMilliseconds);
    }

    private void Wake()
    {
        if (_thread is not null)
        {
            Monitor.Pulse(_gate);
            return;
        }
        if (_disposed) return;
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "HyPlayer decode-ahead",
            Priority = ThreadPriority.AboveNormal
        };
        thread.TrySetApartmentState(ApartmentState.MTA);
        _thread = thread;
        thread.Start();
    }

    private void Run()
    {
        while (true)
        {
            DecodedChunk? chunk = null;
            Action? seekCompleted = null;
            var seek = false;
            var position = TimeSpan.Zero;
            int generation;
            lock (_gate)
            {
                var idle = false;
                while (true)
                {
                    if (_disposed) break;
                    if (_seekRequested)
                    {
                        seek = true;
                        position = _seekPosition;
                        seekCompleted = _seekCompleted;
                        _seekCompleted = null;
                        _seekRequested = false;
                        break;
                    }
                    if (!_ended && _ready.Count < _capacity)
                    {
                        if (_free.Count > 0) chunk = _free.Pop();
                        break;
                    }
                    if (idle)
                    {
                        _thread = null;
                        return;
                    }
                    idle = !Monitor.Wait(_gate, _idleExitMilliseconds);
                }
                if (_disposed) break;
                generation = _generation;
            }

            Exception? error = null;
            Action<DecodedChunk?>[]? ended = null;
            if (seek)
            {
                try
                {
                    _decoder.Seek(position);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                if (error is not null)
                {
                    lock (_gate)
                    {
                        if (!_disposed && generation == _generation) ended = End(error);
                    }
                }
                seekCompleted?.Invoke();
                Complete(ended);
                continue;
            }

            var length = 0;
            var timestamp = TimeSpan.Zero;
            var duration = TimeSpan.Zero;
            try
            {
                chunk ??= _allocate();
                length = _decoder.ReadChunk(chunk.Data, out timestamp, out duration);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Action<DecodedChunk?>? waiter = null;
            lock (_gate)
            {
                if (_disposed) break;
                if (generation != _generation || error is not null || length <= 0)
                {
                    if (chunk is not null) _free.Push(chunk);
                    if (generation == _generation) ended = End(error);
                    chunk = null;
                }
                else
                {
                    chunk!.Length = length;
                    chunk.Timestamp = timestamp;
                    chunk.Duration = duration;
                    if (_waiters.Count > 0) waiter = _waiters.Dequeue();
                    else _ready.Enqueue(chunk);
                }
            }
            waiter?.Invoke(chunk);
            Complete(ended);
        }
        _decoder.Dispose();
    }

    private Action<DecodedChunk?>[] End(Exception? error)
    {
        _ended = true;
        _failure = error;
        var waiters = _waiters.ToArray();
        _waiters.Clear();
        return waiters;
    }

    private static void Complete(Action<DecodedChunk?>[]? waiters)
    {
        if (waiters is null) return;
        foreach (var waiter in waiters) waiter(null);
    }
}
