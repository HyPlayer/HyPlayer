using System;
using System.Threading;
using HyPlayer.UWP.Chopin.Utils;
using Windows.Media;
using Windows.Media.Audio;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public interface IMixRenderSource
    {
        void Render(Span<float> interleavedStereo, int frames);
    }

    internal sealed class CallbackGate
    {
        private int _active;
        private int _closed;

        public bool TryEnter()
        {
            Interlocked.Increment(ref _active);
            if (Volatile.Read(ref _closed) == 0) return true;
            Interlocked.Decrement(ref _active);
            return false;
        }

        public void Exit() => Interlocked.Decrement(ref _active);

        public void Reopen() => Interlocked.Exchange(ref _closed, 0);

        public void CloseAndDrain()
        {
            Interlocked.Exchange(ref _closed, 1);
            var deadline = Environment.TickCount64 + 500;
            var spin = new SpinWait();
            while (Volatile.Read(ref _active) > 0 && Environment.TickCount64 < deadline) spin.SpinOnce();
        }
    }

    public sealed class PlaybackTap
    {
        private readonly float[] _ring;
        private readonly int _capacityFrames;
        private long _totalFrames;
        private readonly Lock _lock = new();

        internal PlaybackTap(AudioFrameOutputNode node, int sampleRate, double seconds)
        {
            Node = node;
            _capacityFrames = (int)(sampleRate * seconds);
            _ring = new float[_capacityFrames * 2];
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

        internal void Pull()
        {
            if (!Gate.TryEnter()) return;
            try
            {
                PullCore();
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
            reference.GetBuffer(out var bytes, out _);
            var data = (float*)bytes;
            var samples = (int)(buffer.Length / 4);
            var span = new ReadOnlySpan<float>(data, samples);
            lock (_lock)
            {
                var frames = samples / 2;
                for (var i = 0; i < frames; i++)
                {
                    var slot = (int)((_totalFrames + i) % _capacityFrames) * 2;
                    _ring[slot] = span[i * 2];
                    _ring[slot + 1] = span[i * 2 + 1];
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

    public sealed class MixRenderer
    {
        private readonly IMixRenderSource _source;
        private readonly CallbackGate _gate = new();
        private float[] _scratch = new float[4096];

        internal MixRenderer(AudioFrameInputNode node, AudioSubmixNode bus, IMixRenderSource source)
        {
            Node = node;
            Bus = bus;
            _source = source;
            node.QuantumStarted += OnQuantumStarted;
        }

        internal AudioFrameInputNode Node { get; }
        internal AudioSubmixNode Bus { get; }

        public long RenderedFrames { get; private set; }

        private void OnQuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
        {
            if (!_gate.TryEnter()) return;
            try
            {
                RenderQuantum(sender, args.RequiredSamples);
            }
            finally
            {
                _gate.Exit();
            }
        }

        private unsafe void RenderQuantum(AudioFrameInputNode sender, int frames)
        {
            if (frames <= 0) return;
            var samples = frames * 2;
            if (_scratch.Length < samples) _scratch = new float[samples * 2];
            var span = new Span<float>(_scratch, 0, samples);
            span.Clear();
            try
            {
                _source.Render(span, frames);
            }
            catch
            {
                span.Clear();
            }
            using var frame = new AudioFrame((uint)(samples * 4));
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (var reference = buffer.CreateReference())
            {
                reference.GetBuffer(out var bytes, out _);
                span.CopyTo(new Span<float>((float*)bytes, samples));
            }
            sender.AddFrame(frame);
            RenderedFrames += frames;
        }

        internal void Detach()
        {
            _gate.CloseAndDrain();
            Node.QuantumStarted -= OnQuantumStarted;
        }
    }
}
