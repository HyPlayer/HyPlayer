using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HyPlayer.UWP.Chopin.Utils;
using Windows.Media;
using Windows.Media.Audio;

namespace HyPlayer.UWP.Chopin.Dsp
{
    internal sealed class CallbackGate
    {
        private int _active;
        private int _closed;

        public CallbackGate(bool closed = false) => _closed = closed ? 1 : 0;

        public bool TryEnter()
        {
            Interlocked.Increment(ref _active);
            if (Volatile.Read(ref _closed) == 0) return true;
            Interlocked.Decrement(ref _active);
            return false;
        }

        public void Exit() => Interlocked.Decrement(ref _active);

        public void Reopen() => Interlocked.Decrement(ref _closed);

        public void CloseAndDrain()
        {
            Interlocked.Increment(ref _closed);
            var deadline = Environment.TickCount64 + 500;
            var spin = new SpinWait();
            while (Volatile.Read(ref _active) > 0 && Environment.TickCount64 < deadline) spin.SpinOnce();
        }
    }

    internal static class AudioFrames
    {
        public static unsafe AudioFrame FromInterleaved(ReadOnlySpan<float> interleavedStereo)
        {
            var bytes = (uint)(interleavedStereo.Length * sizeof(float));
            var frame = new AudioFrame(bytes);
            try
            {
                using var buffer = frame.LockBuffer(AudioBufferAccessMode.Write);
                using var reference = buffer.CreateReference();
                Marshal.ThrowExceptionForHR(reference.GetBuffer(out var data, out var capacity));
                if (data == null || capacity < bytes) throw new InvalidOperationException("The audio frame buffer is smaller than the samples to write.");
                interleavedStereo.CopyTo(new Span<float>(data, interleavedStereo.Length));
                return frame;
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }
    }

    internal static class AudioThreadCallbacks
    {
        private static long _count;

        public static long Count => Interlocked.Read(ref _count);

        public static void Mark() => Interlocked.Increment(ref _count);
    }

    public sealed class PlaybackTap
    {
        private readonly float[] _ring;
        private readonly int _capacityFrames;
        private long _totalFrames;
        private readonly Lock _lock = new();
        private readonly Action<PlaybackTap> _pull;

        public PlaybackTap(int sampleRate, double seconds, Action<PlaybackTap> pull = null)
        {
            _capacityFrames = Math.Max(1, (int)(sampleRate * seconds));
            _ring = new float[_capacityFrames * 2];
            _pull = pull;
        }

        internal PlaybackTap(AudioFrameOutputNode node, int sampleRate, double seconds) : this(sampleRate, seconds)
        {
            Node = node;
        }

        internal AudioFrameOutputNode Node { get; }
        internal CallbackGate Gate { get; } = new();

        public long TotalFrames
        {
            get
            {
                lock (_lock) return _totalFrames;
            }
        }

        public bool Pull()
        {
            if (!Gate.TryEnter()) return false;
            try
            {
                if (Node != null) PullCore();
                else _pull?.Invoke(this);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                Gate.Exit();
            }
        }

        private unsafe void PullCore()
        {
            using var frame = Node.GetFrame();
            using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            if (reference.GetBuffer(out var bytes, out _) < 0 || bytes == null) return;
            var data = (float*)bytes;
            var samples = (int)(buffer.Length / 4);
            Append(new ReadOnlySpan<float>(data, samples));
        }

        public void Append(ReadOnlySpan<float> interleavedStereo)
        {
            lock (_lock)
            {
                var frames = interleavedStereo.Length / 2;
                for (var i = 0; i < frames; i++)
                {
                    var slot = (int)((_totalFrames + i) % _capacityFrames) * 2;
                    _ring[slot] = interleavedStereo[i * 2];
                    _ring[slot + 1] = interleavedStereo[i * 2 + 1];
                }
                _totalFrames += frames;
            }
        }

        public long CopyLatest(Span<float> destination)
        {
            lock (_lock)
            {
                var frames = Math.Min(destination.Length / 2, (int)Math.Min(_totalFrames, _capacityFrames));
                var start = _totalFrames - frames;
                for (var i = 0; i < frames; i++)
                {
                    var slot = (int)((start + i) % _capacityFrames) * 2;
                    destination[i * 2] = _ring[slot];
                    destination[i * 2 + 1] = _ring[slot + 1];
                }
                return start;
            }
        }
    }

    internal interface IMixRendererHost
    {
        void MixQuantaElapsed(MixRenderer renderer, long quanta);
        void MixRendererStopped();
    }

    public sealed class MixRenderer : IMixSink
    {
        private const int WakeMilliseconds = 5;
        private const int JoinMilliseconds = 2000;
        private readonly AudioGraph _graph;
        private readonly IMixPumpSource _source;
        private readonly IMixRendererHost _host;
        private readonly MixPump _pump;
        private readonly Thread _thread;
        private readonly PreciseWait _wait = new();
        private readonly int _quantum;
        private readonly long _callbacksAtStart = AudioThreadCallbacks.Count;
        private volatile bool _stop;
        private volatile bool _exited;
        private int _state;
        private int _unit = 1;

        internal MixRenderer(AudioGraph graph, AudioFrameInputNode node, AudioSubmixNode bus, IMixPumpSource source, IMixRendererHost host)
        {
            _graph = graph;
            Node = node;
            Bus = bus;
            _source = source;
            _host = host;
            _quantum = Math.Max(1, graph.SamplesPerQuantum);
            _pump = new MixPump(this, source, (int)graph.EncodingProperties.SampleRate, quanta => _host.MixQuantaElapsed(this, quanta));
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "AutoMix render"
            };
        }

        internal AudioFrameInputNode Node { get; }
        internal AudioSubmixNode Bus { get; }
        internal bool DrivesRamps => !_exited && Volatile.Read(ref _state) != 2 && !_pump.Failed;

        public MixPumpStats Stats => _pump.Snapshot() with { AudioThreadCallbacks = AudioThreadCallbacks.Count - _callbacksAtStart };

        int IMixSink.QuantumFrames => _quantum;
        long IMixSink.QueuedFrames => (long)Node.QueuedSampleCount / _unit;
        long IMixSink.ClockFrames => (long)_graph.CompletedQuantumCount * _quantum;

        void IMixSink.Write(ReadOnlySpan<float> interleavedStereo)
        {
            using var frame = AudioFrames.FromInterleaved(interleavedStereo);
            Node.AddFrame(frame);
        }

        void IMixSink.Discard() => Node.DiscardQueuedFrames();

        internal void Start()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                var clock = Stopwatch.StartNew();
                _pump.Step(clock.Elapsed.TotalMilliseconds);
                try
                {
                    var written = _pump.WrittenFrames;
                    if (written > 0 && (long)Node.QueuedSampleCount >= written * 2) _unit = 2;
                    Node.Start();
                }
                catch
                {
                }

                var failed = false;
                while (!_stop)
                {
                    _wait.Wait(WakeMilliseconds);
                    if (_stop) break;
                    _pump.Step(clock.Elapsed.TotalMilliseconds);
                    if (failed || !_pump.Failed) continue;
                    failed = true;
                    NotifyStopped();
                }
            }
            catch
            {
            }
            finally
            {
                _exited = true;
                NotifyStopped();
            }
        }

        private void NotifyStopped()
        {
            try
            {
                _host.MixRendererStopped();
            }
            catch
            {
            }
        }

        internal void Shutdown(bool failed)
        {
            var previous = Interlocked.Exchange(ref _state, 2);
            if (previous == 2) return;
            _stop = true;
            var own = ReferenceEquals(Thread.CurrentThread, _thread);
            var joined = previous == 0 || !own && _thread.Join(JoinMilliseconds);
            if (joined) _wait.Dispose();
            try
            {
                Node.DiscardQueuedFrames();
            }
            catch
            {
            }

            if (!failed) return;
            try
            {
                _source.Fail();
            }
            catch
            {
            }
        }
    }
}
