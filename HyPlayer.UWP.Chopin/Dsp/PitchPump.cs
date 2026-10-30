using System;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public sealed class PitchPump
    {
        public const double LeadSeconds = 0.06;
        public const double MaxLeadSeconds = 0.15;

        public delegate void Transform(ReadOnlySpan<float> input, Span<float> output);

        public delegate void Sink(ReadOnlySpan<float> interleavedStereo);

        private readonly int _block;
        private readonly int _lead;
        private readonly int _maxLead;
        private readonly Transform _transform;
        private readonly Sink _sink;
        private readonly float[] _scratch;
        private bool _primed;

        public PitchPump(int blockFrames, int sampleRate, Transform transform, Sink sink)
        {
            _transform = transform ?? throw new ArgumentNullException(nameof(transform));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _block = Math.Max(1, blockFrames);
            var rate = Math.Max(1, sampleRate);
            _lead = Math.Max(_block, (int)(rate * LeadSeconds));
            _maxLead = Math.Max(_lead + _block, (int)(rate * MaxLeadSeconds));
            _scratch = new float[_block * 2];
        }

        public int LeadFrames => _lead;
        public int MaxLeadFrames => _maxLead;
        public long WrittenFrames { get; private set; }
        public long DroppedFrames { get; private set; }

        public void Step(ReadOnlySpan<float> capturedStereo, long queuedFrames)
        {
            if (!_primed)
            {
                _primed = true;
                Array.Clear(_scratch);
                for (var remaining = _lead; remaining > 0; remaining -= _block)
                    Write(_scratch.AsSpan(0, Math.Min(_block, remaining) * 2));
                queuedFrames += _lead;
            }

            var frames = capturedStereo.Length / 2;
            if (frames <= 0) return;
            queuedFrames = Math.Max(0, queuedFrames);
            if (queuedFrames + frames > _maxLead)
            {
                var keep = (int)Math.Clamp(_lead - queuedFrames, 0, frames);
                DroppedFrames += frames - keep;
                capturedStereo = capturedStereo.Slice((frames - keep) * 2);
                frames = keep;
            }

            for (var offset = 0; offset < frames; offset += _block)
            {
                var count = Math.Min(_block, frames - offset);
                var output = _scratch.AsSpan(0, count * 2);
                _transform(capturedStereo.Slice(offset * 2, count * 2), output);
                Write(output);
            }
        }

        private void Write(ReadOnlySpan<float> block)
        {
            _sink(block);
            WrittenFrames += block.Length / 2;
        }
    }
}
