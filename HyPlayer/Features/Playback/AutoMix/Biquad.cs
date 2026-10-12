using System;

namespace HyPlayer.Features.Playback.AutoMix;

public sealed class StereoBiquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _x1L, _x2L, _y1L, _y2L, _x1R, _x2R, _y1R, _y2R;

    public bool Bypass { get; private set; } = true;

    public void SetBypass()
    {
        Bypass = true;
    }

    public void SetHighPass(double frequency, double sampleRate, double q = 0.7071)
    {
        var w0 = 2 * Math.PI * Math.Clamp(frequency, 10, sampleRate * 0.45) / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var a0 = 1 + alpha;
        Set((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
    }

    public void SetLowShelf(double frequency, double gainDb, double sampleRate)
    {
        if (Math.Abs(gainDb) < 0.01)
        {
            SetBypass();
            return;
        }
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        var alpha = sin / 2 * Math.Sqrt(2);
        var sqrtA = Math.Sqrt(a);
        var a0 = (a + 1) + (a - 1) * cos + 2 * sqrtA * alpha;
        Set(a * ((a + 1) - (a - 1) * cos + 2 * sqrtA * alpha) / a0,
            2 * a * ((a - 1) - (a + 1) * cos) / a0,
            a * ((a + 1) - (a - 1) * cos - 2 * sqrtA * alpha) / a0,
            -2 * ((a - 1) + (a + 1) * cos) / a0,
            ((a + 1) + (a - 1) * cos - 2 * sqrtA * alpha) / a0);
    }

    private void Set(double b0, double b1, double b2, double a1, double a2)
    {
        if (Bypass)
        {
            _x1L = _x2L = _y1L = _y2L = _x1R = _x2R = _y1R = _y2R = 0;
        }
        _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2;
        Bypass = false;
    }

    public void Process(Span<float> interleaved, int frames)
    {
        if (Bypass) return;
        for (var i = 0; i < frames; i++)
        {
            double l = interleaved[i * 2];
            var yl = _b0 * l + _b1 * _x1L + _b2 * _x2L - _a1 * _y1L - _a2 * _y2L;
            _x2L = _x1L; _x1L = l; _y2L = _y1L; _y1L = yl;
            double r = interleaved[i * 2 + 1];
            var yr = _b0 * r + _b1 * _x1R + _b2 * _x2R - _a1 * _y1R - _a2 * _y2R;
            _x2R = _x1R; _x1R = r; _y2R = _y1R; _y1R = yr;
            interleaved[i * 2] = (float)yl;
            interleaved[i * 2 + 1] = (float)yr;
        }
    }
}
