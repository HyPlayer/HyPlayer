using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public static class BeatTracker
{
    public sealed class Result
    {
        public double Bpm { get; init; }
        public double Confidence { get; init; }
        public double[] Beats { get; init; } = Array.Empty<double>();
        public double[] Downbeats { get; init; } = Array.Empty<double>();
    }

    public static Result Track(SpectralFeatures features, double minBpm = 60, double maxBpm = 200)
    {
        var onset = features.Onset;
        var fps = 1 / features.HopSeconds;
        if (onset.Length < fps * 8) return new Result();

        var bpm = EstimateTempo(onset, fps, minBpm, maxBpm);
        if (bpm <= 0) return new Result();
        var period = fps * 60 / bpm;
        var frames = TrackDp(onset, period, 100);
        if (frames.Count < 8) return new Result();

        var times = frames.Select(f => Refine(onset, f) * features.HopSeconds).ToArray();
        var ibis = new List<double>();
        for (var i = 1; i < times.Length; i++) ibis.Add(times[i] - times[i - 1]);
        ibis.Sort();
        var medianIbi = ibis[ibis.Count / 2];
        var refinedBpm = 60 / medianIbi;

        var downbeats = EstimateDownbeats(times, features);
        var confidence = EstimateConfidence(onset, frames, ibis, medianIbi);
        return new Result { Bpm = refinedBpm, Confidence = confidence, Beats = times, Downbeats = downbeats };
    }

    private static double EstimateTempo(float[] onset, double fps, double minBpm, double maxBpm)
    {
        var minLag = (int)Math.Floor(fps * 60 / maxBpm);
        var maxLag = (int)Math.Ceiling(fps * 60 / minBpm);
        var n = onset.Length;
        if (maxLag >= n / 2) return 0;
        var ac = new double[maxLag + 2];
        for (var lag = minLag - 1; lag <= maxLag + 1; lag++)
        {
            if (lag <= 0) continue;
            double acc = 0;
            for (var i = lag; i < n; i++) acc += onset[i] * (double)onset[i - lag];
            ac[lag] = acc / (n - lag);
        }
        var bestLag = -1; var best = double.NegativeInfinity;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var bpm = fps * 60 / lag;
            var prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120.0), 2));
            var harmonic = ac[lag] + 0.5 * (2 * lag <= maxLag + 1 ? ac[2 * lag] : 0);
            var score = harmonic * prior;
            if (score > best) { best = score; bestLag = lag; }
        }
        if (bestLag <= 0) return 0;
        var y0 = ac[bestLag - 1]; var y1 = ac[bestLag]; var y2 = ac[bestLag + 1];
        var denom = y0 - 2 * y1 + y2;
        var offset = Math.Abs(denom) > 1e-12 ? 0.5 * (y0 - y2) / denom : 0;
        offset = Math.Clamp(offset, -0.5, 0.5);
        return fps * 60 / (bestLag + offset);
    }

    private static List<int> TrackDp(float[] onset, double period, double tightness)
    {
        var n = onset.Length;
        var local = new double[n];
        var radius = (int)Math.Round(period);
        var kernel = new double[2 * radius + 1];
        for (var k = -radius; k <= radius; k++) kernel[k + radius] = Math.Exp(-0.5 * Math.Pow(k * 32.0 / period, 2));
        for (var i = 0; i < n; i++)
        {
            double acc = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var j = i + k;
                if (j >= 0 && j < n) acc += onset[j] * kernel[k + radius];
            }
            local[i] = acc;
        }

        var cumscore = new double[n];
        var backlink = new int[n];
        var windowStart = (int)Math.Round(2 * period);
        var windowEnd = (int)Math.Round(period / 2);
        var first = true;
        double maxLocal = local.Max();
        for (var i = 0; i < n; i++)
        {
            var bestScore = double.NegativeInfinity;
            var bestIndex = -1;
            for (var lag = windowEnd; lag <= windowStart; lag++)
            {
                var j = i - lag;
                if (j < 0) break;
                var penalty = -tightness * Math.Pow(Math.Log(lag / period), 2);
                var score = cumscore[j] + penalty;
                if (score > bestScore) { bestScore = score; bestIndex = j; }
            }
            if (bestIndex < 0 || (first && local[i] < 0.01 * maxLocal))
            {
                cumscore[i] = local[i];
                backlink[i] = -1;
            }
            else
            {
                cumscore[i] = local[i] + bestScore;
                backlink[i] = bestIndex;
                first = false;
            }
        }

        var maxima = new List<double>();
        for (var i = 1; i < n - 1; i++)
            if (cumscore[i] >= cumscore[i - 1] && cumscore[i] >= cumscore[i + 1]) maxima.Add(cumscore[i]);
        if (maxima.Count == 0) return new List<int>();
        maxima.Sort();
        var threshold = 0.5 * maxima[maxima.Count / 2];
        var tail = -1;
        for (var i = n - 2; i >= 1; i--)
        {
            if (cumscore[i] >= cumscore[i - 1] && cumscore[i] >= cumscore[i + 1] && cumscore[i] > threshold) { tail = i; break; }
        }
        if (tail < 0) return new List<int>();

        var beats = new List<int>();
        for (var b = tail; b >= 0; b = backlink[b]) beats.Add(b);
        beats.Reverse();

        var rms = Math.Sqrt(beats.Average(b => local[b] * local[b]));
        var trim = 0.5 * rms;
        var start = 0; while (start < beats.Count && local[beats[start]] < trim) start++;
        var end = beats.Count - 1; while (end > start && local[beats[end]] < trim) end--;
        return beats.GetRange(start, end - start + 1);
    }

    private static double Refine(float[] onset, int frame)
    {
        var best = frame;
        for (var k = Math.Max(1, frame - 2); k <= Math.Min(onset.Length - 2, frame + 2); k++)
            if (onset[k] > onset[best]) best = k;
        if (best <= 0 || best >= onset.Length - 1) return best;
        double y0 = onset[best - 1], y1 = onset[best], y2 = onset[best + 1];
        var denom = y0 - 2 * y1 + y2;
        if (Math.Abs(denom) < 1e-9) return best;
        return best + Math.Clamp(0.5 * (y0 - y2) / denom, -0.5, 0.5);
    }

    private static double[] EstimateDownbeats(double[] beats, SpectralFeatures features)
    {
        if (beats.Length < 8) return Array.Empty<double>();
        var low = new double[beats.Length];
        var all = new double[beats.Length];
        for (var i = 0; i < beats.Length; i++)
        {
            var f = (int)Math.Round(beats[i] / features.HopSeconds);
            double l = 0, a = 0;
            for (var k = Math.Max(0, f - 2); k <= Math.Min(features.Frames - 1, f + 2); k++)
            {
                l = Math.Max(l, features.LowOnset[k]);
                a = Math.Max(a, features.Onset[k]);
            }
            low[i] = l;
            all[i] = a;
        }
        var bestPhase = 0; var bestScore = double.NegativeInfinity;
        for (var phase = 0; phase < 4; phase++)
        {
            double score = 0; var count = 0;
            for (var i = phase; i < beats.Length; i += 4) { score += 2 * low[i] + all[i]; count++; }
            score /= Math.Max(1, count);
            if (score > bestScore) { bestScore = score; bestPhase = phase; }
        }
        var result = new List<double>();
        for (var i = bestPhase; i < beats.Length; i += 4) result.Add(beats[i]);
        return result.ToArray();
    }

    private static double EstimateConfidence(float[] onset, List<int> frames, List<double> sortedIbis, double medianIbi)
    {
        var mean = onset.Average(v => (double)v);
        var atBeats = frames.Average(f => (double)onset[f]);
        var salience = mean > 0 ? Math.Clamp((atBeats / mean - 1) / 3, 0, 1) : 0;
        var inner = sortedIbis.Skip(sortedIbis.Count / 10).Take(Math.Max(1, sortedIbis.Count * 8 / 10)).ToList();
        var avg = inner.Average();
        var std = Math.Sqrt(inner.Sum(x => (x - avg) * (x - avg)) / Math.Max(1, inner.Count - 1));
        var regularity = Math.Clamp(1 - std / (0.1 * medianIbi), 0, 1);
        return Math.Round(0.5 * salience + 0.5 * regularity, 3);
    }
}
