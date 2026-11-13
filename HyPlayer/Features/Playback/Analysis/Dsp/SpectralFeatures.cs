using System;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public sealed class SpectralFeatures
{
    public const int FrameRate = 100;

    public required int Frames { get; init; }
    public required double HopSeconds { get; init; }
    public required float[] Onset { get; init; }
    public required float[] LowOnset { get; init; }
    public required float[] MidEnergy { get; init; }
    public required float[] Rms { get; init; }

    public static SpectralFeatures Compute(float[] mono, int sampleRate)
    {
        const int nFft = 1024;
        var hop = sampleRate / FrameRate;
        var frames = Math.Max(1, mono.Length / hop);
        var fft = new Fft(nFft);
        var window = Fft.Hann(nFft);
        var re = new float[nFft];
        var im = new float[nFft];
        var bins = nFft / 2 + 1;
        var binHz = sampleRate / (double)nFft;
        var bandCount = 40;
        var bandEdges = new int[bandCount + 1];
        for (var b = 0; b <= bandCount; b++)
        {
            var hz = 30 * Math.Pow(11000 / 30.0, b / (double)bandCount);
            bandEdges[b] = Math.Clamp((int)Math.Round(hz / binHz), 1, bins - 1);
        }
        var lowLimit = (int)Math.Round(150 / binHz);
        var midLow = (int)Math.Round(200 / binHz);
        var midHigh = (int)Math.Round(2000 / binHz);

        var onset = new float[frames];
        var lowOnset = new float[frames];
        var mid = new float[frames];
        var rms = new float[frames];
        var prevBands = new float[bandCount];
        var bands = new float[bandCount];
        float prevLow = 0;
        var mag = new float[bins];

        for (var f = 0; f < frames; f++)
        {
            var center = f * hop;
            double energy = 0;
            for (var i = 0; i < nFft; i++)
            {
                var idx = center + i - nFft / 2;
                var v = idx >= 0 && idx < mono.Length ? mono[idx] : 0f;
                re[i] = v * window[i];
                im[i] = 0;
                if (i >= nFft / 2 - hop / 2 && i < nFft / 2 + hop / 2) energy += v * (double)v;
            }
            rms[f] = (float)Math.Sqrt(energy / hop);
            fft.Forward(re, im);
            for (var k = 0; k < bins; k++) mag[k] = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);

            double flux = 0;
            for (var b = 0; b < bandCount; b++)
            {
                double acc = 0;
                var from = bandEdges[b];
                var to = Math.Max(from + 1, bandEdges[b + 1]);
                for (var k = from; k < to; k++) acc += mag[k];
                bands[b] = (float)Math.Log(1 + 100 * acc / (to - from));
                var d = bands[b] - prevBands[b];
                if (d > 0) flux += d;
                prevBands[b] = bands[b];
            }
            onset[f] = (float)flux;

            double low = 0;
            for (var k = 1; k <= lowLimit; k++) low += mag[k];
            var lowLog = (float)Math.Log(1 + 100 * low / Math.Max(1, lowLimit));
            lowOnset[f] = Math.Max(0, lowLog - prevLow);
            prevLow = lowLog;

            double m = 0;
            for (var k = midLow; k <= midHigh; k++) m += mag[k] * (double)mag[k];
            mid[f] = (float)m;
        }

        RemoveLocalMean(onset, FrameRate);
        RemoveLocalMean(lowOnset, FrameRate);
        return new SpectralFeatures { Frames = frames, HopSeconds = hop / (double)sampleRate, Onset = onset, LowOnset = lowOnset, MidEnergy = mid, Rms = rms };
    }

    private static void RemoveLocalMean(float[] x, int window)
    {
        var copy = (float[])x.Clone();
        double sum = 0;
        var half = window / 2;
        for (var i = 0; i < Math.Min(copy.Length, half); i++) sum += copy[i];
        var count = Math.Min(copy.Length, half);
        for (var i = 0; i < x.Length; i++)
        {
            var add = i + half;
            if (add < copy.Length) { sum += copy[add]; count++; }
            var remove = i - half - 1;
            if (remove >= 0) { sum -= copy[remove]; count--; }
            var mean = count > 0 ? sum / count : 0;
            x[i] = (float)Math.Max(0, copy[i] - mean);
        }
        float max = 0;
        foreach (var v in x) if (v > max) max = v;
        if (max <= 0) return;
        for (var i = 0; i < x.Length; i++) x[i] /= max;
    }
}
