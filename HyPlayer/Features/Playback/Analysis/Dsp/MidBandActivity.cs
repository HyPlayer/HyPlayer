using System;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public static class MidBandActivity
{
    public const string Id = "midband";
    public const double EnterThreshold = 0.2;
    public const double ExitThreshold = 0.1;
    public const double StrongEnterThreshold = 0.3;
    public const double StrongExitThreshold = 0.15;

    private const int FftSize = 1024;
    private const int FrameRate = 50;
    private const int Bands = 12;
    private const int MedianFrames = 7;
    private const int RiseFrames = 4;
    private const double LowHz = 400;
    private const double HighHz = 3400;
    private const double SmoothSeconds = 0.12;
    private const double DenseSeconds = 0.5;
    private const double SteadyRise = 0.25;
    private const double FloorRatio = 0.1;
    private const double Gain = 7;

    public static VocalActivity? Analyze(float[] mono, int sampleRate, int fromSample, int sampleCount, double offsetSeconds, double trackLoudness)
    {
        fromSample = Math.Clamp(fromSample, 0, mono.Length);
        sampleCount = Math.Min(sampleCount, mono.Length - fromSample);
        var hop = sampleRate / FrameRate;
        if (hop <= 0 || sampleCount < sampleRate * 3) return null;
        var frames = sampleCount / hop;
        var binHz = sampleRate / (double)FftSize;
        var edges = new int[Bands + 1];
        for (var b = 0; b <= Bands; b++)
            edges[b] = Math.Clamp((int)Math.Round(LowHz * Math.Pow(HighHz / LowHz, b / (double)Bands) / binHz), 1, FftSize / 2 - 1);

        var fft = new Fft(FftSize);
        var window = Fft.Hann(FftSize);
        double windowEnergy = 0;
        foreach (var w in window) windowEnergy += w * (double)w;
        var normalization = 2 / (FftSize * windowEnergy);
        var re = new float[FftSize];
        var im = new float[FftSize];
        var amplitude = new float[frames * Bands];
        for (var f = 0; f < frames; f++)
        {
            var start = fromSample + f * hop + hop / 2 - FftSize / 2;
            for (var i = 0; i < FftSize; i++)
            {
                var index = start + i;
                re[i] = index >= 0 && index < mono.Length ? mono[index] * window[i] : 0f;
                im[i] = 0;
            }
            fft.Forward(re, im);
            var row = f * Bands;
            for (var b = 0; b < Bands; b++)
            {
                double power = 0;
                var to = Math.Max(edges[b] + 1, edges[b + 1]);
                for (var k = edges[b]; k < to; k++) power += re[k] * (double)re[k] + im[k] * (double)im[k];
                amplitude[row + b] = (float)Math.Sqrt(power * normalization);
            }
        }

        var sustained = new float[frames * Bands];
        Span<float> sorted = stackalloc float[MedianFrames];
        var half = MedianFrames / 2;
        for (var f = 0; f < frames; f++)
        {
            for (var b = 0; b < Bands; b++)
            {
                var count = 0;
                for (var k = Math.Max(0, f - half); k <= Math.Min(frames - 1, f + half); k++)
                {
                    var value = amplitude[k * Bands + b];
                    var slot = count++;
                    while (slot > 0 && sorted[slot - 1] > value)
                    {
                        sorted[slot] = sorted[slot - 1];
                        slot--;
                    }
                    sorted[slot] = value;
                }
                sustained[f * Bands + b] = sorted[count / 2];
            }
        }

        var rise = new double[frames];
        var level = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double squares = 0, rising = 0;
            var row = f * Bands;
            var earlier = Math.Max(0, f - RiseFrames) * Bands;
            for (var b = 0; b < Bands; b++)
            {
                double value = sustained[row + b];
                squares += value * value;
                double before = sustained[earlier + b];
                var delta = value - before - SteadyRise * before;
                if (delta > 0) rising += delta * delta;
            }
            level[f] = Math.Sqrt(squares);
            rise[f] = Math.Sqrt(rising);
        }

        var radius = (int)Math.Round(SmoothSeconds * FrameRate);
        var smoothLevel = Smooth(level, radius);
        var excess = Smooth(rise, radius);
        var smoothRise = Median(excess, (int)Math.Round(DenseSeconds * FrameRate));
        double loudest = 0;
        foreach (var value in smoothLevel) loudest = Math.Max(loudest, value);
        var reference = trackLoudness > -60 ? Math.Pow(10, (trackLoudness + 0.691) / 20) : loudest;
        var values = new double[(int)(frames * VocalActivity.Rate / FrameRate)];
        if (reference <= 1e-6 || loudest <= 1e-6) return new VocalActivity(offsetSeconds, values) { Analyzer = Id };
        var floor = loudest * FloorRatio;
        for (var i = 0; i < values.Length; i++)
        {
            var frame = Math.Min(frames - 1, (int)(i * FrameRate / VocalActivity.Rate));
            if (smoothLevel[frame] <= floor) continue;
            values[i] = Math.Round(Math.Min(1, Gain * smoothRise[frame] / reference), 3);
        }
        return new VocalActivity(offsetSeconds, values) { Analyzer = Id };
    }

    private static double[] Median(double[] values, int radius)
    {
        var result = new double[values.Length];
        var sorted = new double[2 * radius + 1];
        for (var i = 0; i < values.Length; i++)
        {
            var count = 0;
            for (var k = i - radius; k <= i + radius; k++)
            {
                var value = k >= 0 && k < values.Length ? values[k] : 0;
                var slot = count++;
                while (slot > 0 && sorted[slot - 1] > value)
                {
                    sorted[slot] = sorted[slot - 1];
                    slot--;
                }
                sorted[slot] = value;
            }
            result[i] = sorted[count / 2];
        }
        return result;
    }

    private static double[] Smooth(double[] values, int radius)
    {
        var result = new double[values.Length];
        double sum = 0;
        var count = 0;
        for (var i = 0; i < values.Length + radius; i++)
        {
            if (i < values.Length)
            {
                sum += values[i];
                count++;
            }
            if (i - 2 * radius - 1 >= 0)
            {
                sum -= values[i - 2 * radius - 1];
                count--;
            }
            var center = i - radius;
            if (center >= 0 && center < values.Length) result[center] = sum / Math.Max(1, count);
        }
        return result;
    }
}
