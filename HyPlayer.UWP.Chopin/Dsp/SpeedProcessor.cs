using System;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public sealed class SpeedProcessor : IDisposable
    {
        public const double FastGainDbPerOctave = 1.1;
        public const double SlowGainDbPerOctave = 0.8;
        private const double UnitTolerance = 0.001;
        private const double FadeInSeconds = 0.003;
        private const int HistoryLength = 512;

        private readonly int _rate;
        private readonly int _block;
        private readonly PeakLimiter _limiter;
        private readonly float[] _previous;
        private readonly int[] _latency = { -1, -1, -1, -1 };
        private readonly long[] _historyOutput = new long[HistoryLength];
        private readonly double[] _historySource = new double[HistoryLength];
        private readonly double[] _historyRate = new double[HistoryLength];
        private int _historyHead;
        private int _historyCount;
        private float[] _ring;
        private long _ringStart;
        private int _ringCount;
        private long _end = long.MaxValue;
        private Path _path;
        private double _gain = 1;
        private long _output;
        private bool _disposed;

        public SpeedProcessor(int sampleRate, int blockFrames, double speed = 1, bool preservePitch = true)
        {
            _rate = Math.Max(1, sampleRate);
            _block = Math.Max(1, blockFrames);
            _limiter = new PeakLimiter(_rate);
            _previous = new float[_block * 2];
            _ring = new float[Math.Max(_rate, _block * 16) * 2];
            SetSpeed(speed, preservePitch);
            _gain = TargetGain();
        }

        public double Speed { get; private set; } = 1;
        public bool PreservePitch { get; private set; } = true;
        public long BufferedEnd => _ringStart + _ringCount;
        public bool Ended => _end != long.MaxValue;
        public long EndFrame => _end;
        public long OutputFrames => _output;
        public int DelayFrames => _limiter.DelayFrames;

        public double DownstreamGain
        {
            set => _limiter.Level = value > 0 && double.IsFinite(value)
                ? (float)Math.Min(PeakLimiter.Ceiling / value, 1e6)
                : 1e6f;
        }

        public void SetSpeed(double speed, bool preservePitch)
        {
            if (!double.IsFinite(speed)) speed = 1;
            Speed = Math.Clamp(speed, SpeedResampler.MinSpeed, SpeedResampler.MaxSpeed);
            PreservePitch = preservePitch;
            if (_path != null && _path.Kind == KindFor()) _path.SetSpeed(Speed);
        }

        public void Append(ReadOnlySpan<float> interleavedStereo)
        {
            var frames = interleavedStereo.Length / 2;
            if (frames <= 0 || Ended) return;
            Reserve(frames);
            interleavedStereo.Slice(0, frames * 2).CopyTo(_ring.AsSpan(_ringCount * 2));
            _ringCount += frames;
        }

        public void MarkEnd(long endFrame)
        {
            if (Ended) return;
            _end = Math.Clamp(endFrame, _ringStart, BufferedEnd);
            _ringCount = (int)(_end - _ringStart);
        }

        public void Reset()
        {
            _path?.Dispose();
            _path = null;
            _ringStart = 0;
            _ringCount = 0;
            _end = long.MaxValue;
            _historyCount = 0;
            _output = 0;
            _limiter.Reset();
            _gain = TargetGain();
        }

        public long RequiredInput(int outputFrames)
        {
            outputFrames = Math.Max(0, outputFrames);
            var kind = KindFor();
            if (_path == null) return StartRequired(kind, _ringStart, outputFrames);
            if (_path.Kind == kind) return _path.Required(outputFrames);
            var at = (long)Math.Round(_path.Audible);
            return Math.Max(_path.Required(_block), StartRequired(kind, at, outputFrames));
        }

        public bool Render(Span<float> output)
        {
            var frames = output.Length / 2;
            if (frames <= 0 || frames > _block) throw new ArgumentOutOfRangeException(nameof(output));
            var kind = KindFor();
            double start;
            double rate;
            if (_path == null)
            {
                var at = _ringStart;
                if (!Ensure(StartRequired(kind, at, frames))) return false;
                _path = Create(kind);
                _path.Activate(at);
                start = _path.Audible;
                rate = _path.Rate;
                _path.Render(output);
                FadeIn(output);
            }
            else if (_path.Kind != kind)
            {
                var at = (long)Math.Round(_path.Audible);
                if (!Ensure(Math.Max(_path.Required(frames), StartRequired(kind, at, frames)))) return false;
                var previous = _previous.AsSpan(0, frames * 2);
                _path.Render(previous);
                var next = Create(kind);
                next.Activate(at);
                start = at;
                rate = next.Rate;
                next.Render(output);
                CrossFade(previous, output);
                _path.Dispose();
                _path = next;
            }
            else
            {
                if (!Ensure(_path.Required(frames))) return false;
                start = _path.Audible;
                rate = _path.Rate;
                _path.Render(output);
            }

            ApplyGain(output);
            _limiter.Process(output);
            Record(_output, start, rate);
            _output += frames;
            Trim();
            return true;
        }

        public double SourceAt(long outputFrame)
        {
            if (_historyCount == 0) return _ringStart;
            var frame = outputFrame - _limiter.DelayFrames;
            for (var i = 0; i < _historyCount; i++)
            {
                var slot = (_historyHead - 1 - i + HistoryLength) % HistoryLength;
                if (_historyOutput[slot] > frame && i < _historyCount - 1) continue;
                return Math.Max(0, _historySource[slot] + Math.Max(0, frame - _historyOutput[slot]) * _historyRate[slot]);
            }

            return _historySource[(_historyHead - _historyCount + HistoryLength) % HistoryLength];
        }

        public bool Drained(long outputFrame) => Ended && SourceAt(outputFrame) >= _end;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _path?.Dispose();
            _path = null;
        }

        private Kind KindFor()
        {
            if (Math.Abs(Speed - 1) <= UnitTolerance) return Kind.Copy;
            if (!PreservePitch) return Kind.Resample;
            return Speed > 1 ? Kind.Fast : Kind.Slow;
        }

        private Path Create(Kind kind) => kind switch
        {
            Kind.Copy => new CopyPath(this),
            Kind.Resample => new ResamplePath(this),
            _ => new StretchPath(this, kind)
        };

        private long StartRequired(Kind kind, long at, int frames) => kind switch
        {
            Kind.Copy => at + frames,
            Kind.Resample => at + (long)Math.Ceiling(frames * Speed) + 2 * SpeedResampler.LatencyBound + 4,
            _ => at + SeekLength(kind) + (long)Math.Ceiling(frames * Speed) + 2
        };

        private int SeekLength(Kind kind)
        {
            var slot = kind == Kind.Fast ? 0 : 1;
            if (_latency[slot * 2] < 0)
            {
                using var probe = StretchPath.Build(_rate, kind);
                _latency[slot * 2] = probe.OutputSeekLength(0);
                _latency[slot * 2 + 1] = probe.OutputSeekLength(1) - _latency[slot * 2];
            }

            return _latency[slot * 2] + (int)Math.Ceiling(Speed * _latency[slot * 2 + 1]);
        }

        private bool Ensure(long required)
        {
            if (required <= BufferedEnd) return true;
            if (!Ended) return false;
            var missing = (int)(required - BufferedEnd);
            Reserve(missing);
            _ring.AsSpan(_ringCount * 2, missing * 2).Clear();
            _ringCount += missing;
            return true;
        }

        private ReadOnlySpan<float> Input(long from, int frames) =>
            _ring.AsSpan((int)(from - _ringStart) * 2, frames * 2);

        private void Reserve(int frames)
        {
            if ((_ringCount + frames) * 2 <= _ring.Length) return;
            var grown = new float[Math.Max(_ring.Length * 2, (_ringCount + frames) * 2)];
            _ring.AsSpan(0, _ringCount * 2).CopyTo(grown);
            _ring = grown;
        }

        private void Trim()
        {
            if (_path == null) return;
            var keep = (long)Math.Floor(Math.Min(_path.Audible, _path.Consumed)) - _block;
            var drop = (int)Math.Min(keep - _ringStart, _ringCount);
            if (drop <= 0) return;
            _ring.AsSpan(drop * 2, (_ringCount - drop) * 2).CopyTo(_ring);
            _ringCount -= drop;
            _ringStart += drop;
        }

        private void Record(long output, double source, double rate)
        {
            _historyOutput[_historyHead] = output;
            _historySource[_historyHead] = source;
            _historyRate[_historyHead] = rate;
            _historyHead = (_historyHead + 1) % HistoryLength;
            if (_historyCount < HistoryLength) _historyCount++;
        }

        private double TargetGain()
        {
            var kind = KindFor();
            var perOctave = kind switch
            {
                Kind.Fast => FastGainDbPerOctave,
                Kind.Slow => SlowGainDbPerOctave,
                _ => 0
            };
            return Math.Pow(10, perOctave * Math.Abs(Math.Log2(Speed)) / 20);
        }

        private void ApplyGain(Span<float> output)
        {
            var target = TargetGain();
            var frames = output.Length / 2;
            var from = _gain;
            if (Math.Abs(target - 1) < 1e-6 && Math.Abs(from - 1) < 1e-6) return;
            for (var i = 0; i < frames; i++)
            {
                var g = (float)(from + (target - from) * (i + 1) / frames);
                output[i * 2] *= g;
                output[i * 2 + 1] *= g;
            }

            _gain = target;
        }

        private void FadeIn(Span<float> output)
        {
            var frames = Math.Min(output.Length / 2, Math.Max(1, (int)(_rate * FadeInSeconds)));
            for (var i = 0; i < frames; i++)
            {
                var g = (float)(i + 1) / (frames + 1);
                output[i * 2] *= g;
                output[i * 2 + 1] *= g;
            }
        }

        private static void CrossFade(ReadOnlySpan<float> previous, Span<float> output)
        {
            var frames = output.Length / 2;
            for (var i = 0; i < frames; i++)
            {
                var t = (i + 0.5) / frames;
                var fadeIn = (float)Math.Sin(t * Math.PI / 2);
                var fadeOut = (float)Math.Cos(t * Math.PI / 2);
                output[i * 2] = output[i * 2] * fadeIn + previous[i * 2] * fadeOut;
                output[i * 2 + 1] = output[i * 2 + 1] * fadeIn + previous[i * 2 + 1] * fadeOut;
            }
        }

        private enum Kind
        {
            Copy,
            Fast,
            Slow,
            Resample
        }

        private abstract class Path : IDisposable
        {
            protected Path(SpeedProcessor owner, Kind kind)
            {
                Owner = owner;
                Kind = kind;
            }

            protected SpeedProcessor Owner { get; }
            public Kind Kind { get; }
            public abstract double Audible { get; }
            public abstract double Consumed { get; }
            public abstract double Rate { get; }
            public abstract long Required(int frames);
            public abstract void Activate(long at);
            public abstract void Render(Span<float> output);

            public virtual void SetSpeed(double speed)
            {
            }

            public virtual void Dispose()
            {
            }
        }

        private sealed class CopyPath : Path
        {
            private long _next;

            public CopyPath(SpeedProcessor owner) : base(owner, Kind.Copy)
            {
            }

            public override double Audible => _next;
            public override double Consumed => _next;
            public override double Rate => 1;
            public override long Required(int frames) => _next + frames;
            public override void Activate(long at) => _next = at;

            public override void Render(Span<float> output)
            {
                var frames = output.Length / 2;
                Owner.Input(_next, frames).CopyTo(output);
                _next += frames;
            }
        }

        private sealed class StretchPath : Path
        {
            private readonly TimeStretcher _stretcher;
            private double _speed;
            private int _seekLength;
            private long _fed;
            private double _carry;

            public StretchPath(SpeedProcessor owner, Kind kind) : base(owner, kind)
            {
                _stretcher = Build(owner._rate, kind);
                _speed = owner.Speed;
                _seekLength = _stretcher.OutputSeekLength(_speed);
            }

            public override double Audible => _fed + _carry - _seekLength;
            public override double Consumed => _fed;
            public override double Rate => _speed;

            public override long Required(int frames) => _fed + (long)Math.Floor(_carry + _speed * frames) + 1;

            public override void Activate(long at)
            {
                _seekLength = _stretcher.OutputSeekLength(_speed);
                _stretcher.OutputSeek(Owner.Input(at, _seekLength));
                _fed = at + _seekLength;
                _carry = 0;
            }

            public override void SetSpeed(double speed)
            {
                _speed = speed;
                _seekLength = _stretcher.OutputSeekLength(speed);
            }

            public override void Render(Span<float> output)
            {
                var wanted = _carry + _speed * (output.Length / 2);
                var take = (int)Math.Floor(wanted);
                _carry = wanted - take;
                _stretcher.Process(Owner.Input(_fed, take), output);
                _fed += take;
            }

            public override void Dispose() => _stretcher.Dispose();

            public static TimeStretcher Build(int rate, Kind kind) =>
                kind == Kind.Fast
                    ? new TimeStretcher(2, rate, PitchPreservingSpeed.CompressBlockSeconds, PitchPreservingSpeed.CompressIntervalSeconds)
                    : new TimeStretcher(2, rate);
        }

        private sealed class ResamplePath : Path
        {
            private const int Chunk = 256;
            private readonly SpeedResampler _resampler;
            private readonly float[] _scratch;
            private float[] _queue;
            private int _queued;
            private long _fed;
            private double _audible;
            private double _speed;

            public ResamplePath(SpeedProcessor owner) : base(owner, Kind.Resample)
            {
                _resampler = new SpeedResampler(Chunk);
                _scratch = new float[_resampler.MaxOutputFrames(Chunk) * 2];
                _queue = new float[(owner._block + _resampler.MaxOutputFrames(Chunk)) * 2];
                _speed = owner.Speed;
                _resampler.SetSpeed(1 / _speed);
            }

            public override double Audible => _audible;
            public override double Consumed => _fed;
            public override double Rate => _speed;

            public override long Required(int frames) =>
                _fed + Math.Max(0, (long)Math.Ceiling((frames - _queued) * _speed)) + SpeedResampler.LatencyBound + 4;

            public override void Activate(long at)
            {
                _resampler.Reset();
                _fed = at;
                _audible = at;
                _queued = 0;
            }

            public override void SetSpeed(double speed)
            {
                _speed = speed;
                _resampler.SetSpeed(1 / speed);
            }

            public override void Render(Span<float> output)
            {
                var frames = output.Length / 2;
                while (_queued < frames)
                {
                    var take = (int)Math.Min(Chunk, Owner.BufferedEnd - _fed);
                    if (take <= 0) break;
                    var produced = _resampler.Process(Owner.Input(_fed, take), _scratch);
                    _fed += take;
                    if ((_queued + produced) * 2 > _queue.Length) Array.Resize(ref _queue, (_queued + produced) * 2);
                    _scratch.AsSpan(0, produced * 2).CopyTo(_queue.AsSpan(_queued * 2));
                    _queued += produced;
                }

                var available = Math.Min(frames, _queued);
                _queue.AsSpan(0, available * 2).CopyTo(output);
                output.Slice(available * 2).Clear();
                _queue.AsSpan(available * 2, (_queued - available) * 2).CopyTo(_queue);
                _queued -= available;
                _audible += frames * _speed;
            }
        }
    }
}
