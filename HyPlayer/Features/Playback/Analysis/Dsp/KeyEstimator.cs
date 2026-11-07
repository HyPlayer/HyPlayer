using System;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public static class KeyEstimator
{
    private static readonly double[] Major = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] Minor = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    public static (int key, double confidence) Estimate(float[] mono, int sampleRate)
    {
        const int nFft = 4096;
        var hop = nFft / 2;
        var fft = new Fft(nFft);
        var window = Fft.Hann(nFft);
        var re = new float[nFft];
        var im = new float[nFft];
        var binHz = sampleRate / (double)nFft;
        var pitchClass = new int[nFft / 2];
        for (var k = 0; k < nFft / 2; k++)
        {
            var hz = k * binHz;
            pitchClass[k] = hz < 80 || hz > 2000 ? -1 : ((int)Math.Round(12 * Math.Log2(hz / 440.0)) % 12 + 12 + 9) % 12;
        }
        var chroma = new double[12];
        for (var start = 0; start + nFft <= mono.Length; start += hop)
        {
            for (var i = 0; i < nFft; i++) { re[i] = mono[start + i] * window[i]; im[i] = 0; }
            fft.Forward(re, im);
            var frame = new double[12];
            double total = 0;
            for (var k = 1; k < nFft / 2; k++)
            {
                var pc = pitchClass[k];
                if (pc < 0) continue;
                var m = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                frame[pc] += m;
                total += m;
            }
            if (total <= 1e-9) continue;
            for (var c = 0; c < 12; c++) chroma[c] += Math.Log(1 + 10 * frame[c] / total);
        }

        var best = -1; var bestScore = double.NegativeInfinity; var second = double.NegativeInfinity;
        for (var key = 0; key < 24; key++)
        {
            var profile = key < 12 ? Major : Minor;
            var tonic = key % 12;
            var score = Correlate(chroma, profile, tonic);
            if (score > bestScore) { second = bestScore; bestScore = score; best = key; }
            else if (score > second) second = score;
        }
        return (best, Math.Round(Math.Max(0, bestScore - second), 3));
    }

    private static double Correlate(double[] chroma, double[] profile, int tonic)
    {
        double meanC = 0, meanP = 0;
        for (var i = 0; i < 12; i++) { meanC += chroma[i]; meanP += profile[i]; }
        meanC /= 12; meanP /= 12;
        double num = 0, dc = 0, dp = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[(i + tonic) % 12] - meanC;
            var p = profile[i] - meanP;
            num += c * p; dc += c * c; dp += p * p;
        }
        return num / Math.Sqrt(dc * dp + 1e-12);
    }

    public static int CamelotDistance(int keyA, int keyB)
    {
        if (keyA < 0 || keyB < 0) return -1;
        var (numA, minorA) = Camelot(keyA);
        var (numB, minorB) = Camelot(keyB);
        var diff = Math.Abs(numA - numB);
        diff = Math.Min(diff, 12 - diff);
        return diff + (minorA == minorB ? 0 : 1);
    }

    private static (int number, bool minor) Camelot(int key)
    {
        var minor = key >= 12;
        var tonic = key % 12;
        var relativeMajor = minor ? (tonic + 3) % 12 : tonic;
        var number = (relativeMajor * 7 % 12 + 8) % 12;
        return (number, minor);
    }
}
