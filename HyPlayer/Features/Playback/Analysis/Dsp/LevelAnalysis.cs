using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public static class LevelAnalysis
{
    public static double IntegratedLoudness(float[] mono, int sampleRate)
    {
        var (pb, pa) = HighShelf(sampleRate);
        var (rb, ra) = HighPass(sampleRate);
        var filtered = new double[mono.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, z1 = 0, z2 = 0;
        for (var i = 0; i < mono.Length; i++)
        {
            double x = mono[i];
            var y = pb[0] * x + pb[1] * x1 + pb[2] * x2 - pa[1] * y1 - pa[2] * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            var z = rb[0] * y + rb[1] * u1 + rb[2] * u2 - ra[1] * z1 - ra[2] * z2;
            u2 = u1; u1 = y; z2 = z1; z1 = z;
            filtered[i] = z;
        }

        var block = (int)(0.4 * sampleRate);
        var step = (int)(0.1 * sampleRate);
        var powers = new List<double>();
        for (var start = 0; start + block <= filtered.Length; start += step)
        {
            double acc = 0;
            for (var i = start; i < start + block; i++) acc += filtered[i] * filtered[i];
            powers.Add(acc / block);
        }
        if (powers.Count == 0) return -70;
        static double Lufs(double p) => -0.691 + 10 * Math.Log10(p + 1e-12);
        var absGated = powers.Where(p => Lufs(p) > -70).ToList();
        if (absGated.Count == 0) return -70;
        var relativeThreshold = Lufs(absGated.Average()) - 10;
        var gated = absGated.Where(p => Lufs(p) > relativeThreshold).ToList();
        return Math.Round(gated.Count == 0 ? -70 : Lufs(gated.Average()), 2);
    }

    private static (double[] b, double[] a) HighShelf(double fs)
    {
        const double f0 = 1681.974450955533;
        const double g = 3.999843853973347;
        const double q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * f0 / fs);
        var vh = Math.Pow(10, g / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        return (new[] { (vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0 },
                new[] { 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0 });
    }

    private static (double[] b, double[] a) HighPass(double fs)
    {
        const double f0 = 38.13547087602444;
        const double q = 0.5003270373238773;
        var k = Math.Tan(Math.PI * f0 / fs);
        var a0 = 1 + k / q + k * k;
        return (new[] { 1.0, -2.0, 1.0 }, new[] { 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0 });
    }

    public static (double start, double end) SoundBounds(float[] mono, int sampleRate, double thresholdDb = -50)
    {
        var window = Math.Max(1, sampleRate / 100);
        var threshold = Math.Pow(10, thresholdDb / 20);
        var count = mono.Length / window;
        int first = -1, last = -1;
        for (var w = 0; w < count; w++)
        {
            double acc = 0;
            for (var i = w * window; i < (w + 1) * window; i++) acc += mono[i] * (double)mono[i];
            if (Math.Sqrt(acc / window) < threshold) continue;
            if (first < 0) first = w;
            last = w;
        }
        if (first < 0) return (0, mono.Length / (double)sampleRate);
        return (first * window / (double)sampleRate, (last + 1) * window / (double)sampleRate);
    }

    public static double[] Envelope(float[] rms, double hopSeconds, double rate)
    {
        var stride = Math.Max(1, (int)Math.Round(1 / (rate * hopSeconds)));
        var result = new double[rms.Length / stride];
        for (var i = 0; i < result.Length; i++)
        {
            double acc = 0;
            for (var k = i * stride; k < (i + 1) * stride; k++) acc += rms[k] * (double)rms[k];
            result[i] = Math.Round(20 * Math.Log10(Math.Sqrt(acc / stride) + 1e-9), 1);
        }
        return result;
    }

    public static double[] BarEnergies(SpectralFeatures features, double[] downbeats)
    {
        if (downbeats.Length < 2) return Array.Empty<double>();
        var bars = new double[downbeats.Length - 1];
        for (var b = 0; b < bars.Length; b++)
        {
            var from = (int)(downbeats[b] / features.HopSeconds);
            var to = Math.Min(features.Frames, (int)(downbeats[b + 1] / features.HopSeconds));
            double acc = 0;
            for (var f = from; f < to; f++) acc += features.MidEnergy[f];
            bars[b] = Math.Round(10 * Math.Log10(acc / Math.Max(1, to - from) + 1e-12), 2);
        }
        return bars;
    }

    public static (double introEnd, double outroStart) IntroOutro(double[] barEnergies, double[] downbeats)
    {
        if (barEnergies.Length < 8) return (-1, -1);
        var sorted = barEnergies.OrderBy(v => v).ToArray();
        var reference = sorted[(int)(0.75 * (sorted.Length - 1))];
        bool Full(int i) => i >= 0 && i < barEnergies.Length && barEnergies[i] >= reference - 4;
        bool Section(int i)
        {
            var hits = 0;
            for (var k = i; k < i + 4; k++) if (Full(k)) hits++;
            return hits >= 3;
        }
        var intro = -1;
        for (var i = 0; i + 3 < barEnergies.Length; i++)
        {
            if (!Section(i)) continue;
            intro = i;
            break;
        }
        var outro = -1;
        for (var i = barEnergies.Length - 4; i >= 0; i--)
        {
            if (!Section(i)) continue;
            outro = i + 4;
            break;
        }
        if (intro >= 0) intro = intro / 4 * 4;
        if (outro >= 0) outro = Math.Min(barEnergies.Length, (outro + 3) / 4 * 4);
        var introTime = intro > 0 && intro < downbeats.Length ? downbeats[intro] : -1;
        var outroTime = outro > 0 && outro < downbeats.Length ? downbeats[outro] : -1;
        return (introTime, outroTime);
    }
}
