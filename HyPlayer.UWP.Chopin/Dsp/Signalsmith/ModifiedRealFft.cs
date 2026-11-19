// Port of Signalsmith Linear RealFFT<float, false, true>. See LICENSE.txt.
using System;

namespace HyPlayer.UWP.Chopin.Dsp.Signalsmith;

/// <summary>Half-bin shifted real FFT using a half-size complex transform.</summary>
internal sealed class ModifiedRealFft
{
    private readonly MixedRadixFft _fft;
    private readonly Complex32[] _packed, _transformed, _twists, _twiddles;
    private readonly int _half;
    private sealed class Plan
    {
        public readonly Complex32[] Twists, Twiddles;
        public readonly MixedRadixFft.Plan Complex;
        public long Bytes => 128 + Complex.Bytes + (Twists.LongLength + Twiddles.LongLength) * 8;
        public Plan(int size)
        {
            var half = size / 2;
            Complex = new MixedRadixFft.Plan(half);
            Twists = new Complex32[half]; Twiddles = new Complex32[half / 2 + 1];
            for (var i = 0; i < half; i++) Twists[i] = Complex32.Polar((float)(-2 * Math.PI * i / size));
            for (var i = 0; i < Twiddles.Length; i++) Twiddles[i] = Complex32.Polar((float)((i + 0.5) * (-2 * Math.PI / size) - Math.PI / 2));
        }
    }
    private static readonly BoundedPlanCache<int, Plan> Plans = new(static size => new Plan(size), static plan => plan.Bytes, 2 * 1024 * 1024);

    public ModifiedRealFft(int size)
    {
        var plan = Plans.Get(size);
        _half = size / 2;
        _fft = new MixedRadixFft(plan.Complex);
        _packed = new Complex32[_half]; _transformed = new Complex32[_half];
        _twists = plan.Twists; _twiddles = plan.Twiddles;
    }
    public void Forward(ReadOnlySpan<float> time, Span<Complex32> spectrum)
    {
        for (var i = 0; i < _half; i++) _packed[i] = new Complex32(time[2 * i], time[2 * i + 1]) * _twists[i];
        _fft.Forward(_packed, _transformed);
        for (var i = 0; i < _twiddles.Length; i++)
        {
            var mirror = _half - 1 - i;
            var odd = (_transformed[i] + _transformed[mirror].Conjugate) * 0.5f;
            var even = (_transformed[i] - _transformed[mirror].Conjugate) * 0.5f * _twiddles[i];
            spectrum[i] = odd + even;
            spectrum[mirror] = new Complex32(odd.Real - even.Real, even.Imaginary - odd.Imaginary);
        }
    }
    public void Inverse(ReadOnlySpan<Complex32> spectrum, Span<float> time)
    {
        for (var i = 0; i < _twiddles.Length; i++)
        {
            var mirror = _half - 1 - i;
            var odd = spectrum[i] + spectrum[mirror].Conjugate;
            var even = (spectrum[i] - spectrum[mirror].Conjugate) * _twiddles[i].Conjugate;
            _packed[i] = odd + even;
            _packed[mirror] = new Complex32(odd.Real - even.Real, even.Imaginary - odd.Imaginary);
        }
        _fft.Inverse(_packed, _transformed);
        for (var i = 0; i < _half; i++)
        {
            var sample = _transformed[i] * _twists[i].Conjugate;
            time[2 * i] = sample.Real; time[2 * i + 1] = sample.Imaginary;
        }
    }
}
