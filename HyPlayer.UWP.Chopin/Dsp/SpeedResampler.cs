using System;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public sealed class SpeedResampler
    {
        public const double MinSpeed = 0.25;
        public const double MaxSpeed = 4;
        private const int Zeros = 16;
        private const int Phases = 512;
        private const double Beta = 8.5;
        private const double Passband = 0.97;
        private static readonly int MaxHalf = (int)Math.Ceiling(Zeros / (MinSpeed * Passband));

        private readonly int _maxInput;
        private readonly float[] _history;
        private readonly float[] _table;
        private int _count;
        private double _position;
        private double _step = 1;
        private double _cutoff;
        private int _half;
        private int _taps;

        public SpeedResampler(int maxInputFrames)
        {
            _maxInput = Math.Max(1, maxInputFrames);
            _history = new float[(_maxInput + 2 * MaxHalf + 4) * 2];
            _table = new float[(Phases + 1) * 2 * MaxHalf];
            _count = MaxHalf;
            _position = MaxHalf;
            SetSpeed(1);
        }

        public static int LatencyBound => MaxHalf;

        public int LatencyFrames => _half;

        public int MaxOutputFrames(int inputFrames) => (int)Math.Ceiling(inputFrames * MaxSpeed) + 2;

        public void SetSpeed(double speed)
        {
            speed = Math.Clamp(double.IsFinite(speed) ? speed : 1, MinSpeed, MaxSpeed);
            _step = 1 / speed;
            var cutoff = Passband * Math.Min(1, speed);
            if (Math.Abs(cutoff - _cutoff) < 1e-9) return;
            _cutoff = cutoff;
            _half = (int)Math.Ceiling(Zeros / cutoff);
            _taps = 2 * _half;
            BuildTable();
        }

        public void Reset()
        {
            Array.Clear(_history);
            _count = MaxHalf;
            _position = MaxHalf;
        }

        public int Process(ReadOnlySpan<float> interleavedStereo, Span<float> output)
        {
            var written = 0;
            while (interleavedStereo.Length >= 2)
            {
                var frames = Math.Min(_maxInput, interleavedStereo.Length / 2);
                if (output.Length / 2 - written < MaxOutputFrames(frames))
                    throw new ArgumentException("The output buffer is too small for the input.", nameof(output));
                written += ProcessChunk(interleavedStereo.Slice(0, frames * 2), output.Slice(written * 2));
                interleavedStereo = interleavedStereo.Slice(frames * 2);
            }

            return written;
        }

        private int ProcessChunk(ReadOnlySpan<float> input, Span<float> output)
        {
            input.CopyTo(_history.AsSpan(_count * 2));
            _count += input.Length / 2;
            var capacity = output.Length / 2;
            var written = 0;
            var history = _history;
            var table = _table;
            var taps = _taps;
            while (written < capacity)
            {
                var index = (int)_position;
                if (index + _half >= _count) break;
                var phase = (_position - index) * Phases;
                var row = (int)phase;
                var weight = (float)(phase - row);
                var lower = row * taps;
                var upper = lower + taps;
                var sample = (index - _half + 1) * 2;
                float left = 0, right = 0;
                for (var tap = 0; tap < taps; tap++, sample += 2)
                {
                    var low = table[lower + tap];
                    var coefficient = low + (table[upper + tap] - low) * weight;
                    left += history[sample] * coefficient;
                    right += history[sample + 1] * coefficient;
                }

                output[written * 2] = left;
                output[written * 2 + 1] = right;
                written++;
                _position += _step;
            }

            var drop = Math.Min((int)_position - MaxHalf + 1, _count);
            if (drop > 0)
            {
                _history.AsSpan(drop * 2, (_count - drop) * 2).CopyTo(_history);
                _count -= drop;
                _position -= drop;
            }

            return written;
        }

        private void BuildTable()
        {
            var norm = BesselI0(Beta);
            for (var row = 0; row <= Phases; row++)
            {
                var offset = row * _taps;
                var fraction = (double)row / Phases;
                double sum = 0;
                for (var tap = 0; tap < _taps; tap++)
                {
                    var t = tap - _half + 1 - fraction;
                    var x = _cutoff * t;
                    var sinc = Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
                    var r = t / _half;
                    var window = Math.Abs(r) >= 1 ? 0 : BesselI0(Beta * Math.Sqrt(1 - r * r)) / norm;
                    var value = _cutoff * sinc * window;
                    _table[offset + tap] = (float)value;
                    sum += value;
                }

                var scale = (float)(1 / sum);
                for (var tap = 0; tap < _taps; tap++) _table[offset + tap] *= scale;
            }
        }

        private static double BesselI0(double x)
        {
            double sum = 1, term = 1;
            var quarter = x * x / 4;
            for (var k = 1; k < 50; k++)
            {
                term *= quarter / (k * k);
                sum += term;
                if (term < sum * 1e-12) break;
            }

            return sum;
        }
    }
}
