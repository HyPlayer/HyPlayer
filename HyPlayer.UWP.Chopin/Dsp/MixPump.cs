using System;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public interface IMixSink
    {
        int QuantumFrames { get; }
        long QueuedFrames { get; }
        long ClockFrames { get; }
        void Write(ReadOnlySpan<float> interleavedStereo);
        void Discard();
    }

    public readonly struct MixClock
    {
        public MixClock(long played, int quantum, bool stable)
        {
            Played = played;
            Quantum = quantum;
            Stable = stable;
        }

        public long Played { get; }
        public int Quantum { get; }
        public bool Stable { get; }
    }

    public interface IMixPumpSource
    {
        void Capture();
        void Advance(in MixClock clock);
        bool TakeCut(out int fadeFrames);
        void Render(Span<float> interleavedStereo, int frames);
        void Fail();
    }

    public sealed class ClockOffsetEstimator
    {
        private const int Window = 32;
        private const int Recent = 4;
        private const int Minimum = 8;
        private readonly long[] _values = new long[Window];
        private int _count;
        private int _next;

        public void Reset()
        {
            _count = 0;
            _next = 0;
        }

        public void Add(long offset)
        {
            _values[_next] = offset;
            _next = (_next + 1) % Window;
            if (_count < Window) _count++;
        }

        public bool TryGet(out long offset)
        {
            offset = 0;
            if (_count < Minimum) return false;
            var latest = _values[(_next + Window - 1) % Window];
            for (var i = 2; i <= Recent; i++)
                if (_values[(_next + Window - i) % Window] != latest) return false;
            var agree = 0;
            for (var i = 0; i < _count; i++)
                if (_values[i] == latest) agree++;
            if (agree * 4 < _count * 3) return false;
            offset = latest;
            return true;
        }
    }

    public readonly struct MixPumpStats
    {
        public long Wakes { get; init; }
        public long StarvedFrames { get; init; }
        public long LowWaterWakes { get; init; }
        public long EmptyWakes { get; init; }
        public double MinLeadMs { get; init; }
        public double MedianLeadMs { get; init; }
        public double MaxWakeGapMs { get; init; }
        public long AudioThreadCallbacks { get; init; }
        public int QuantumFrames { get; init; }
        public int SampleRate { get; init; }
        public bool Failed { get; init; }
    }

    public sealed class MixPump
    {
        public const double TargetLeadSeconds = 0.2;
        public const double LowWaterSeconds = 0.05;
        private const double ShadowSeconds = 0.6;
        private const int LeadBucketMs = 5;
        private const int LeadBuckets = 128;

        private readonly IMixSink _sink;
        private readonly IMixPumpSource _source;
        private readonly int _sampleRate;
        private readonly int _block;
        private readonly int _targetLead;
        private readonly int _lowWater;
        private readonly float[] _scratch;
        private readonly float[] _shadow;
        private readonly int _shadowFrames;
        private readonly ClockOffsetEstimator _drift = new();
        private readonly long[] _leadHistogram = new long[LeadBuckets];
        private long _written;
        private bool _primed;
        private bool _frozen;
        private bool _failed;
        private bool _hasBaseline;
        private long _baseline;
        private long _starved;
        private long _wakes;
        private long _lowWaterWakes;
        private long _emptyWakes;
        private long _minLead = long.MaxValue;
        private double _lastWakeMs = double.NaN;
        private double _maxGapMs;

        public MixPump(IMixSink sink, IMixPumpSource source, int sampleRate)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _sampleRate = Math.Max(1, sampleRate);
            _block = Math.Max(1, sink.QuantumFrames);
            _targetLead = (int)(_sampleRate * TargetLeadSeconds);
            _lowWater = (int)(_sampleRate * LowWaterSeconds);
            _scratch = new float[_block * 2];
            _shadowFrames = Math.Max(_targetLead + _block * 4, (int)(_sampleRate * ShadowSeconds));
            _shadow = new float[_shadowFrames * 2];
        }

        public bool Failed => _failed;
        public long WrittenFrames => _written;

        public void Step(double nowMs)
        {
            if (_primed && !_frozen && !double.IsNaN(_lastWakeMs))
                _maxGapMs = Math.Max(_maxGapMs, nowMs - _lastWakeMs);
            _lastWakeMs = nowMs;
            if (_failed) return;
            try
            {
                StepCore();
            }
            catch
            {
                _failed = true;
                try
                {
                    _source.Fail();
                }
                catch
                {
                }
            }
        }

        private void StepCore()
        {
            var before = _sink.ClockFrames;
            _source.Capture();
            var queued = _sink.QueuedFrames;
            var clock = _sink.ClockFrames;
            var stable = before == clock;
            var played = _written - queued;
            Observe(queued, clock - played, stable);
            _source.Advance(new MixClock(played, _block, stable));
            if (_source.TakeCut(out var fadeFrames)) Cut(fadeFrames);
            while (_written - played < _targetLead)
            {
                var span = _scratch.AsSpan();
                span.Clear();
                try
                {
                    _source.Render(span, _block);
                }
                catch
                {
                    span.Clear();
                }

                _sink.Write(span);
                Remember(span);
                _written += _block;
            }

            _primed = true;
        }

        private void Observe(long queued, long drift, bool stable)
        {
            if (!_primed || _frozen) return;
            _wakes++;
            if (queued < _minLead) _minLead = queued;
            if (queued < _lowWater) _lowWaterWakes++;
            if (queued <= 0) _emptyWakes++;
            var bucket = (int)Math.Clamp(queued * 1000 / _sampleRate / LeadBucketMs, 0, LeadBuckets - 1);
            _leadHistogram[bucket]++;
            if (!stable) return;
            _drift.Add(drift);
            if (!_drift.TryGet(out var value)) return;
            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _baseline = value;
            }
            else if (value - _baseline > _starved)
            {
                _starved = value - _baseline;
            }
        }

        private void Remember(ReadOnlySpan<float> block)
        {
            var frames = block.Length / 2;
            for (var i = 0; i < frames; i++)
            {
                var slot = (int)((_written + i) % _shadowFrames) * 2;
                _shadow[slot] = block[i * 2];
                _shadow[slot + 1] = block[i * 2 + 1];
            }
        }

        private void Cut(int fadeFrames)
        {
            var queued = Math.Max(0, _sink.QueuedFrames);
            _sink.Discard();
            _written -= queued;
            _frozen = true;
            var tail = (int)Math.Min(Math.Min(Math.Max(0, fadeFrames), queued), _shadowFrames);
            if (tail <= 0) return;
            var faded = new float[tail * 2];
            for (var i = 0; i < tail; i++)
            {
                var slot = (int)((_written + i) % _shadowFrames) * 2;
                var gain = 1f - (i + 1) / (float)tail;
                faded[i * 2] = _shadow[slot] * gain;
                faded[i * 2 + 1] = _shadow[slot + 1] * gain;
            }

            _sink.Write(faded);
            _written += tail;
        }

        public MixPumpStats Snapshot()
        {
            var median = 0d;
            long seen = 0;
            for (var i = 0; i < LeadBuckets && _wakes > 0; i++)
            {
                seen += _leadHistogram[i];
                if (seen * 2 < _wakes) continue;
                median = i * LeadBucketMs;
                break;
            }

            return new MixPumpStats
            {
                Wakes = _wakes,
                StarvedFrames = _starved,
                LowWaterWakes = _lowWaterWakes,
                EmptyWakes = _emptyWakes,
                MinLeadMs = _minLead == long.MaxValue ? 0 : _minLead * 1000.0 / _sampleRate,
                MedianLeadMs = median,
                MaxWakeGapMs = _maxGapMs,
                QuantumFrames = _block,
                SampleRate = _sampleRate,
                Failed = _failed
            };
        }
    }
}
