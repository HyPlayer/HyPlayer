using System;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public sealed class Fft
{
    private readonly int _n;
    private readonly int[] _bitReverse;
    private readonly float[] _cos;
    private readonly float[] _sin;

    public Fft(int n)
    {
        if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("FFT size must be a power of two", nameof(n));
        _n = n;
        _bitReverse = new int[n];
        var bits = (int)Math.Log2(n);
        for (var i = 0; i < n; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _bitReverse[i] = r;
        }
        _cos = new float[n / 2];
        _sin = new float[n / 2];
        for (var i = 0; i < n / 2; i++)
        {
            _cos[i] = (float)Math.Cos(-2 * Math.PI * i / n);
            _sin[i] = (float)Math.Sin(-2 * Math.PI * i / n);
        }
    }

    public int Size => _n;

    public void Forward(float[] re, float[] im)
    {
        var n = _n;
        for (var i = 0; i < n; i++)
        {
            var j = _bitReverse[i];
            if (j <= i) continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }
        for (var size = 2; size <= n; size <<= 1)
        {
            var half = size >> 1;
            var step = n / size;
            for (var start = 0; start < n; start += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }

    public static float[] Hann(int n)
    {
        var w = new float[n];
        for (var i = 0; i < n; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
        return w;
    }
}
