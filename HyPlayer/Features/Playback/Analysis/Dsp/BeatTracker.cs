using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public static class BeatTracker
{
    private const double MinPulseSeconds = 0.25;
    private const double MaxPulseSeconds = 1.5;
    private const double GridToleranceSeconds = 0.02;
    private const double MinLift = 1.45;
    private const double MinAlternation = 0.2;
    private const double GroupingRatio = 0.7;
    private const double PhantomShare = 0.6;
    private const double SteadyStability = 0.8;
    private const double SpanSeconds = 20;
    private const double SpanToleranceSeconds = 0.025;
    private const double OnsetLatencySeconds = 0.011;
    private const int Reach = 2;

    public sealed class Result
    {
        public double Bpm { get; init; }
        public double Confidence { get; init; }
        public double[] Beats { get; init; } = Array.Empty<double>();
        public double[] Downbeats { get; init; } = Array.Empty<double>();
        public bool Steady { get; init; }
        public double Stability { get; init; }
        public double FirstHalfBpm { get; init; }
        public double SecondHalfBpm { get; init; }
        public double DownbeatConfidence { get; init; }
    }

    private readonly record struct PulseLevel(double Period, double Share, double Lift, double Half, double Third, double Alternation, bool Pulse);

    private readonly record struct GridFit(double Period, double Phase, double Stability, double FirstHalfPeriod, double SecondHalfPeriod);

    public static Result Track(SpectralFeatures features)
    {
        var onset = features.Onset;
        var fps = 1 / features.HopSeconds;
        if (onset.Length < fps * 8) return new Result();
        var pulse = FindPulse(onset, fps);
        return (pulse > 0 ? TrackSteady(features, fps, pulse) : null) ?? TrackFree(features, fps, pulse);
    }

    private static Result? TrackSteady(SpectralFeatures features, double fps, double period)
    {
        var onset = features.Onset;
        var grid = FitGrid(onset, fps, period);
        if (grid.Stability < SteadyStability) return null;

        var n = onset.Length;
        var first = (int)Math.Ceiling((Reach - grid.Phase) / grid.Period);
        var last = (int)Math.Floor((n - 1 - Reach - grid.Phase) / grid.Period);
        var count = last - first + 1;
        if (count < 8) return null;
        var strength = new double[count];
        double squares = 0, atBeats = 0;
        for (var i = 0; i < count; i++)
        {
            var frame = (int)Math.Round(grid.Phase + (first + i) * grid.Period);
            double best = 0;
            for (var k = frame - Reach; k <= frame + Reach; k++) best = Math.Max(best, onset[k]);
            strength[i] = best;
            squares += best * best;
            atBeats += Math.Max(onset[frame], Math.Max(onset[frame - 1], onset[frame + 1]));
        }
        var trim = 0.5 * Math.Sqrt(squares / count);
        var start = 0;
        while (start < count && strength[start] < trim) start++;
        var end = count - 1;
        while (end > start && strength[end] < trim) end--;
        if (end - start + 1 < 8) return null;

        var times = new double[end - start + 1];
        for (var i = 0; i < times.Length; i++)
            times[i] = (grid.Phase + (first + start + i) * grid.Period) * features.HopSeconds + OnsetLatencySeconds;

        var (downbeats, downbeatConfidence) = EstimateDownbeats(times, features);
        var salience = Salience(onset, atBeats / count);
        return new Result
        {
            Bpm = fps * 60 / grid.Period,
            Confidence = Math.Round(grid.Stability * (0.5 + 0.5 * salience), 3),
            Beats = times,
            Downbeats = downbeats,
            Steady = true,
            Stability = Math.Round(grid.Stability, 3),
            FirstHalfBpm = fps * 60 / grid.FirstHalfPeriod,
            SecondHalfBpm = fps * 60 / grid.SecondHalfPeriod,
            DownbeatConfidence = downbeatConfidence
        };
    }

    private static Result TrackFree(SpectralFeatures features, double fps, double pulse)
    {
        var onset = features.Onset;
        var bpm = pulse > 0 ? fps * 60 / pulse : EstimateTempo(onset, fps, 60, 200);
        if (bpm <= 0) return new Result();
        var frames = TrackDp(onset, fps * 60 / bpm, 100);
        if (frames.Count < 8) return new Result();

        var times = frames.Select(f => Refine(onset, f) * features.HopSeconds + OnsetLatencySeconds).ToArray();
        var ibis = new double[times.Length - 1];
        for (var i = 1; i < times.Length; i++) ibis[i - 1] = times[i] - times[i - 1];
        Array.Sort(ibis);
        var medianIbi = ibis[ibis.Length / 2];

        var (period, stability) = FitSpans(times, 0, times.Length, medianIbi);
        var half = times.Length / 2;
        var (firstPeriod, _) = FitSpans(times, 0, half, period);
        var (secondPeriod, _) = FitSpans(times, half, times.Length, period);
        var (downbeats, downbeatConfidence) = EstimateDownbeats(times, features);
        var salience = Salience(onset, frames.Average(f => (double)onset[f]));
        return new Result
        {
            Bpm = 60 / period,
            Confidence = Math.Round(Math.Min(stability * (0.5 + 0.5 * salience), stability >= SteadyStability ? 1 : 0.5), 3),
            Beats = times,
            Downbeats = downbeats,
            Steady = stability >= SteadyStability,
            Stability = Math.Round(stability, 3),
            FirstHalfBpm = 60 / firstPeriod,
            SecondHalfBpm = 60 / secondPeriod,
            DownbeatConfidence = downbeatConfidence
        };
    }

    private static double Salience(float[] onset, double atBeats)
    {
        double mean = 0;
        foreach (var value in onset) mean += value;
        mean /= onset.Length;
        return mean > 0 ? Math.Clamp((atBeats / mean - 1) / 3, 0, 1) : 0;
    }

    private static double FindPulse(float[] onset, double fps)
    {
        var coarse = StrongestLag(onset, (int)(fps * 0.2), (int)(fps * 2.0));
        if (coarse <= 0) return 0;
        var seed = RefineBySpectrum(onset, fps, coarse);
        double total = 0;
        foreach (var value in onset) total += value;
        if (total <= 0) return 0;

        var minPeriod = fps * MinPulseSeconds;
        var maxPeriod = fps * MaxPulseSeconds;
        var scratch = new double[5 * ((int)Math.Ceiling(maxPeriod) + 2)];
        var sorted = new double[(int)Math.Ceiling(maxPeriod) + 2];
        var best = 0.0;
        var bestScore = 0.0;
        foreach (var factor in new[] { 1.0, 3.0, 1 / 3.0, 5.0, 0.2 })
        {
            for (var octave = -5; octave <= 5; octave++)
            {
                var period = seed * factor * Math.Pow(2, octave);
                if (period < minPeriod || period > maxPeriod) continue;
                var level = Measure(onset, period, total, scratch, sorted);
                if (!level.Pulse) continue;
                var bpm = fps * 60 / period;
                var distance = Math.Log2(bpm / 120) / 0.7;
                var resonance = Math.Exp(-0.5 * distance * distance);
                var evidence = Math.Sqrt((0.3 + 0.7 * level.Alternation) * (1 - Math.Min(1, Math.Max(level.Half, level.Third))));
                var score = resonance * evidence;
                if (score <= bestScore) continue;
                bestScore = score;
                best = period;
            }
        }
        return best;
    }

    private static double StrongestLag(float[] onset, int minLag, int maxLag)
    {
        var n = onset.Length;
        if (maxLag + 1 >= n / 2) return 0;
        double mean = 0;
        foreach (var value in onset) mean += value;
        mean /= n;
        var centered = new double[n];
        for (var i = 0; i < n; i++) centered[i] = onset[i] - mean;
        var ac = new double[maxLag + 2];
        for (var lag = minLag - 1; lag <= maxLag + 1; lag++)
        {
            double acc = 0;
            for (var i = lag; i < n; i++) acc += centered[i] * centered[i - lag];
            ac[lag] = acc / (n - lag);
        }
        var bestLag = -1;
        var best = 0.0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            if (ac[lag] <= ac[lag - 1] || ac[lag] < ac[lag + 1] || ac[lag] <= best) continue;
            best = ac[lag];
            bestLag = lag;
        }
        if (bestLag < 0) return 0;
        var denom = ac[bestLag - 1] - 2 * ac[bestLag] + ac[bestLag + 1];
        var offset = Math.Abs(denom) > 1e-12 ? 0.5 * (ac[bestLag - 1] - ac[bestLag + 1]) / denom : 0;
        return bestLag + Math.Clamp(offset, -0.5, 0.5);
    }

    private static double RefineBySpectrum(float[] onset, double fps, double period)
    {
        var n = onset.Length;
        var size = 1;
        while (size < 4 * n && size < 1 << 21) size <<= 1;
        if (size < n) return period;
        var re = new float[size];
        var im = new float[size];
        double mean = 0;
        foreach (var value in onset) mean += value;
        mean /= n;
        for (var i = 0; i < n; i++) re[i] = (float)(onset[i] - mean);
        new Fft(size).Forward(re, im);
        var power = new double[size / 2];
        for (var k = 0; k < power.Length; k++) power[k] = re[k] * (double)re[k] + im[k] * (double)im[k];

        var harmonics = Math.Clamp((int)Math.Floor(10 * period / fps), 1, 24);
        double Score(double stretch)
        {
            double total = 0;
            for (var h = 1; h <= harmonics; h++)
            {
                var position = h * (1 + stretch) / period * size;
                var k = (int)position;
                if (k + 1 >= power.Length) break;
                var fraction = position - k;
                total += power[k] * (1 - fraction) + power[k + 1] * fraction;
            }
            return total;
        }

        var step = 0.5 * period / ((double)size * harmonics);
        var bestStretch = 0.0;
        var bestScore = double.NegativeInfinity;
        for (var stretch = -0.012; stretch <= 0.012; stretch += step)
        {
            var score = Score(stretch);
            if (score <= bestScore) continue;
            bestScore = score;
            bestStretch = stretch;
        }
        var before = Score(bestStretch - step);
        var after = Score(bestStretch + step);
        var curvature = before - 2 * bestScore + after;
        if (Math.Abs(curvature) > 1e-12) bestStretch += step * Math.Clamp(0.5 * (before - after) / curvature, -0.5, 0.5);
        return period / (1 + bestStretch);
    }

    private static void Fold(float[] onset, int from, int to, double period, int bins, double[] profile)
    {
        Array.Clear(profile, 0, bins);
        var scale = bins / period;
        for (var t = from; t < to; t++)
        {
            var value = onset[t];
            if (value <= 0) continue;
            var position = t * scale;
            var whole = Math.Floor(position);
            var fraction = position - whole;
            var bin = (int)(whole % bins);
            profile[bin] += value * (1 - fraction);
            profile[bin + 1 == bins ? 0 : bin + 1] += value * fraction;
        }
    }

    private static double Smoothed(double[] profile, int bins, int index)
    {
        index = (index % bins + bins) % bins;
        var previous = index == 0 ? bins - 1 : index - 1;
        var next = index + 1 == bins ? 0 : index + 1;
        return 0.25 * profile[previous] + 0.5 * profile[index] + 0.25 * profile[next];
    }

    private static PulseLevel Measure(float[] onset, double period, double total, double[] scratch, double[] sorted)
    {
        var bins = Math.Max(8, (int)Math.Round(period));
        Fold(onset, 0, onset.Length, period, bins, scratch);
        var index = 0;
        var top = double.NegativeInfinity;
        for (var i = 0; i < bins; i++)
        {
            var value = Smoothed(scratch, bins, i);
            sorted[i] = value;
            if (value <= top) continue;
            top = value;
            index = i;
        }
        Array.Sort(sorted, 0, bins);
        var floor = sorted[bins / 4];

        double Around(double offset)
        {
            var center = index + (int)Math.Round(offset * bins);
            double best = 0;
            for (var k = -Reach; k <= Reach; k++) best = Math.Max(best, Smoothed(scratch, bins, center + k) - floor);
            return best;
        }

        var peak = Around(0);
        if (peak <= 0) return default;
        double mass = 0;
        for (var k = -Reach; k <= Reach; k++) mass += scratch[((index + k) % bins + bins) % bins];
        var share = mass / total;
        var lift = share * bins / (2 * Reach + 1);
        var half = Around(0.5) / peak;
        var thirdLow = Math.Min(Around(1 / 3.0), Around(2 / 3.0)) / peak;
        var thirdHigh = Math.Max(Around(1 / 3.0), Around(2 / 3.0)) / peak;
        var fifth = Math.Min(Math.Min(Around(0.2), Around(0.4)), Math.Min(Around(0.6), Around(0.8))) / peak;
        var pulse = lift >= MinLift && thirdLow < GroupingRatio && fifth < GroupingRatio;
        if (!pulse) return new PulseLevel(period, share, lift, half, thirdHigh, 0, false);

        Fold(onset, 0, onset.Length, 2 * period, 2 * bins, scratch);
        double Paired(int center)
        {
            double best = 0;
            for (var k = -Reach; k <= Reach + 1; k++) best = Math.Max(best, Smoothed(scratch, 2 * bins, center + k) - floor / 2);
            return best;
        }
        var even = Paired(index);
        var odd = Paired(index + bins);
        var alternation = Math.Min(even, odd) / Math.Max(1e-12, Math.Max(even, odd));
        if (alternation < MinAlternation) pulse = false;

        if (pulse)
        {
            foreach (var group in new[] { 3, 5 })
            {
                Fold(onset, 0, onset.Length, group * period, group * bins, scratch);
                double strongest = 0;
                for (var g = 0; g < group; g++)
                {
                    double groupMass = 0;
                    for (var k = -Reach; k <= Reach; k++) groupMass += scratch[((index + g * bins + k) % (group * bins) + group * bins) % (group * bins)];
                    strongest = Math.Max(strongest, groupMass);
                }
                if (strongest / total >= PhantomShare * share) pulse = false;
            }
        }
        return new PulseLevel(period, share, lift, half, thirdHigh, alternation, pulse);
    }

    private static GridFit FitGrid(float[] onset, double fps, double period)
    {
        var n = onset.Length;
        var tolerance = GridToleranceSeconds * fps;
        var capacity = (int)Math.Ceiling(period * 1.05) + 8;
        var template = new double[capacity];
        var window = new double[capacity];
        var smooth = new double[capacity];
        var correlations = new double[capacity];
        var length = Math.Min(n, (int)Math.Max(8 * fps, 12 * period));
        var hop = Math.Max(1, length / 2);
        var count = (n - length) / hop + 1;
        var centers = new double[count];
        var offsets = new double[count];
        var weights = new double[count];
        double phase = 0;
        for (var pass = 0; ; pass++)
        {
            var bins = Math.Max(8, (int)Math.Round(period));
            Fold(onset, 0, n, period, bins, window);
            var peak = 0;
            double templateEnergy = 0;
            for (var i = 0; i < bins; i++)
            {
                template[i] = Smoothed(window, bins, i);
                templateEnergy += template[i] * template[i];
                if (template[i] > template[peak]) peak = i;
            }
            phase = (peak + Vertex(template[(peak + bins - 1) % bins], template[peak], template[(peak + 1) % bins])) * period / bins;

            var maxShift = bins / 4;
            for (var w = 0; w < count; w++)
            {
                var from = w * hop;
                Fold(onset, from, from + length, period, bins, window);
                double mass = 0, energy = 0;
                for (var i = 0; i < bins; i++)
                {
                    smooth[i] = Smoothed(window, bins, i);
                    mass += window[i];
                    energy += smooth[i] * smooth[i];
                }
                centers[w] = from + length / 2.0;
                offsets[w] = 0;
                weights[w] = 0;
                if (energy <= 0 || templateEnergy <= 0) continue;
                var best = 1;
                for (var shift = -maxShift - 1; shift <= maxShift + 1; shift++)
                {
                    double correlation = 0;
                    for (var i = 0; i < bins; i++)
                    {
                        var j = i + shift;
                        if (j < 0) j += bins;
                        else if (j >= bins) j -= bins;
                        correlation += smooth[j] * template[i];
                    }
                    var slot = shift + maxShift + 1;
                    correlations[slot] = correlation;
                    if (slot >= 1 && slot <= 2 * maxShift + 1 && correlation > correlations[best]) best = slot;
                }
                var shiftBins = best - maxShift - 1 + Vertex(correlations[best - 1], correlations[best], correlations[best + 1]);
                offsets[w] = shiftBins * period / bins;
                weights[w] = mass * Math.Max(0, correlations[best]) / Math.Sqrt(energy * templateEnergy);
            }
            if (pass == 1) break;
            period *= 1 + Slope(centers, offsets, weights, 0, count, 3 * tolerance);
        }

        double total = 0, aligned = 0;
        for (var w = 0; w < count; w++)
        {
            total += weights[w];
            if (Math.Abs(offsets[w]) <= tolerance) aligned += weights[w];
        }
        var stability = total > 0 ? aligned / total : 0;
        var firstPeriod = period * (1 + Slope(centers, offsets, weights, 0, count / 2, 3 * tolerance));
        var secondPeriod = period * (1 + Slope(centers, offsets, weights, count / 2, count, 3 * tolerance));
        return new GridFit(period, phase, stability, firstPeriod, secondPeriod);
    }

    private static double Vertex(double before, double center, double after)
    {
        var curvature = before - 2 * center + after;
        return Math.Abs(curvature) > 1e-12 ? Math.Clamp(0.5 * (before - after) / curvature, -0.5, 0.5) : 0;
    }

    private static double Slope(double[] x, double[] y, double[] weights, int from, int to, double limit)
    {
        double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = from; i < to; i++)
        {
            if (weights[i] <= 0 || Math.Abs(y[i]) > limit) continue;
            sw += weights[i];
            sx += weights[i] * x[i];
            sy += weights[i] * y[i];
            sxx += weights[i] * x[i] * x[i];
            sxy += weights[i] * x[i] * y[i];
        }
        var spread = sw * sxx - sx * sx;
        return sw > 0 && spread > 1e-9 * sw * sxx ? (sw * sxy - sx * sy) / spread : 0;
    }

    private static (double period, double stability) FitSpans(double[] times, int from, int to, double guess)
    {
        if (to - from < 4 || guess <= 0) return (guess, 0);
        var span = Math.Min(SpanSeconds, (times[to - 1] - times[from]) / 2);
        if (span < 4 * guess) return (guess, 0);
        var best = guess;
        var bestFit = -1.0;
        var bestStep = int.MaxValue;
        for (var step = -50; step <= 50; step++)
        {
            var period = guess * (1 + step * 0.0002);
            int matched = 0, total = 0, j = from;
            for (var i = from; i < to; i++)
            {
                while (j < to && times[j] - times[i] < span) j++;
                if (j >= to) break;
                total++;
                var distance = times[j] - times[i];
                if (Math.Abs(distance - Math.Round(distance / period) * period) <= SpanToleranceSeconds) matched++;
            }
            if (total == 0) return (guess, 0);
            var fit = matched / (double)total;
            if (fit < bestFit || fit == bestFit && Math.Abs(step) >= bestStep) continue;
            bestFit = fit;
            bestStep = Math.Abs(step);
            best = period;
        }

        double numerator = 0, denominator = 0;
        for (int i = from, j = from; i < to; i++)
        {
            while (j < to && times[j] - times[i] < span) j++;
            if (j >= to) break;
            var distance = times[j] - times[i];
            var beats = Math.Round(distance / best);
            if (Math.Abs(distance - beats * best) > SpanToleranceSeconds) continue;
            numerator += distance * beats;
            denominator += beats * beats;
        }
        return (denominator > 0 ? numerator / denominator : best, bestFit);
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
        return fps * 60 / (bestLag + Vertex(ac[bestLag - 1], ac[bestLag], ac[bestLag + 1]));
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
        return best + Vertex(onset[best - 1], onset[best], onset[best + 1]);
    }

    private static (double[] downbeats, double confidence) EstimateDownbeats(double[] beats, SpectralFeatures features)
    {
        if (beats.Length < 8) return (Array.Empty<double>(), 0);
        var low = new double[beats.Length];
        var power = new double[beats.Length];
        for (var i = 0; i < beats.Length; i++)
        {
            var f = (int)Math.Round((beats[i] - OnsetLatencySeconds) / features.HopSeconds);
            for (var k = Math.Max(0, f - 2); k <= Math.Min(features.Frames - 1, f + 2); k++) low[i] = Math.Max(low[i], features.LowOnset[k]);
            var to = i + 1 < beats.Length ? (int)Math.Round((beats[i + 1] - OnsetLatencySeconds) / features.HopSeconds) : f + 1;
            double acc = 0;
            var count = 0;
            for (var k = Math.Max(0, f); k < Math.Min(features.Frames, to); k++)
            {
                acc += features.Rms[k] * (double)features.Rms[k];
                count++;
            }
            power[i] = acc / Math.Max(1, count);
        }
        double loudest = 0;
        foreach (var value in power) loudest = Math.Max(loudest, value);
        var quiet = loudest * 1e-4;

        Span<double> accent = stackalloc double[4];
        Span<double> change = stackalloc double[4];
        double accentTop = 0, changeTop = 0;
        for (var phase = 0; phase < 4; phase++)
        {
            double sum = 0;
            var count = 0;
            for (var i = phase; i < beats.Length; i += 4)
            {
                sum += low[i];
                count++;
            }
            accent[phase] = sum / Math.Max(1, count);
            accentTop = Math.Max(accentTop, accent[phase]);

            double steps = 0;
            var bars = 0;
            var previous = double.NaN;
            for (var i = phase; i + 4 <= beats.Length; i += 4)
            {
                var bar = 10 * Math.Log10(Math.Max(quiet, (power[i] + power[i + 1] + power[i + 2] + power[i + 3]) / 4) + 1e-30);
                if (!double.IsNaN(previous))
                {
                    steps += (bar - previous) * (bar - previous);
                    bars++;
                }
                previous = bar;
            }
            change[phase] = steps / Math.Max(1, bars);
            changeTop = Math.Max(changeTop, change[phase]);
        }

        var bestPhase = 0;
        double bestScore = -1, secondScore = -1;
        for (var phase = 0; phase < 4; phase++)
        {
            var score = (accentTop > 0 ? accent[phase] / accentTop : 1) * (1 + change[phase]) / (1 + changeTop);
            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestPhase = phase;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }
        var result = new double[(beats.Length - bestPhase + 3) / 4];
        for (var i = 0; i < result.Length; i++) result[i] = beats[bestPhase + 4 * i];
        var confidence = bestScore > 0 && accentTop > 0 ? Math.Clamp((bestScore - Math.Max(0, secondScore)) / bestScore, 0, 1) : 0;
        return (result, Math.Round(confidence, 3));
    }
}
