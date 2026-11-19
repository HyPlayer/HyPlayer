using System;
using System.Collections.Generic;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public static class SectionAnalysis
{
    public const double BoundaryNovelty = 0.3;
    public const double RepeatSimilarity = 0.7;
    public const double RepeatLevelDb = 4;
    public const int KernelUnits = 16;
    public const int MinimumSectionUnits = 8;
    public const double MinimumSectionSeconds = 4;
    public const double TrustedBarConfidence = 0.3;

    private const int FftSize = 4096;
    private const int Hop = 1024;
    private const int Bands = 12;
    private const int Pitches = 12;
    private const int Dimensions = Pitches + Bands + 1;
    private const double BandLowHz = 60;
    private const double BandHighHz = 10000;
    private const double ChromaLowHz = 65;
    private const double ChromaHighHz = 2100;
    private const double FallbackUnitSeconds = 0.5;
    private const double LevelFloorDb = 40;
    private const double BandFloorDb = 50;
    private const int SmoothUnits = 4;
    private const double Bias = 1;
    private const int RefineReach = 3;
    private const int RefineSide = 4;
    private const double ChromaSpread = 0.1;
    private const double BandSpreadDb = 2;
    private const double LevelSpreadDb = 3;
    private const int MinimumSideUnits = 4;
    private const int SnapUnits = 2;
    private const int GridBeats = 16;

    public sealed class Result
    {
        public double[] Units { get; init; } = Array.Empty<double>();
        public double[] Novelty { get; init; } = Array.Empty<double>();
        public TrackSection[] Sections { get; init; } = Array.Empty<TrackSection>();
    }

    public static TrackSection[] Analyze(float[] mono, int sampleRate, double soundStart, double soundEnd,
        double[] beats, double[] downbeats, bool trustBars)
    {
        return Measure(mono, sampleRate, soundStart, soundEnd, beats, downbeats, trustBars).Sections;
    }

    public static Result Measure(float[] mono, int sampleRate, double soundStart, double soundEnd,
        double[] beats, double[] downbeats, bool trustBars)
    {
        var duration = mono.Length / (double)sampleRate;
        soundStart = Math.Clamp(soundStart, 0, duration);
        soundEnd = Math.Clamp(soundEnd <= soundStart ? duration : soundEnd, soundStart, duration);
        var units = Units(soundStart, soundEnd, beats);
        var count = units.Length - 1;
        if (count < 2 * MinimumSideUnits) return Whole(soundStart, soundEnd, units);

        var levels = Levels(mono, sampleRate, units);
        var features = Features(mono, sampleRate, units, levels);
        var novelty = Novelty(features, count);
        var boundaries = Boundaries(novelty, features, units, downbeats, trustBars);
        var sections = Sections(boundaries, units, levels, features, novelty);
        return new Result { Units = units, Novelty = novelty, Sections = sections };
    }

    private static Result Whole(double soundStart, double soundEnd, double[] units)
    {
        if (soundEnd - soundStart <= 0) return new Result { Units = units };
        var section = new TrackSection { Start = Math.Round(soundStart, 3), End = Math.Round(soundEnd, 3) };
        return new Result { Units = units, Novelty = new double[Math.Max(0, units.Length - 1)], Sections = [section] };
    }

    private static double[] Units(double start, double end, double[] beats)
    {
        var units = new List<double> { start };
        var step = FallbackUnitSeconds;
        var inside = new List<double>();
        if (beats.Length >= GridBeats)
        {
            var ibis = new double[beats.Length - 1];
            for (var i = 1; i < beats.Length; i++) ibis[i - 1] = beats[i] - beats[i - 1];
            Array.Sort(ibis);
            var typical = ibis[ibis.Length / 2];
            if (typical > 0.2 && typical < 2.5)
            {
                step = typical;
                foreach (var beat in beats)
                    if (beat > start + 0.25 * step && beat < end - 0.25 * step) inside.Add(beat);
            }
        }

        void Fill(double to)
        {
            var gap = to - units[^1];
            var pieces = Math.Max(1, (int)Math.Round(gap / step));
            var from = units[^1];
            for (var k = 1; k < pieces; k++) units.Add(from + gap * k / pieces);
        }

        foreach (var beat in inside)
        {
            if (beat - units[^1] > 1.75 * step) Fill(beat);
            units.Add(beat);
        }

        Fill(end);
        if (end - units[^1] < 0.25 * step && units.Count > 1) units[^1] = end;
        else units.Add(end);
        return units.ToArray();
    }

    private static double[] Levels(float[] mono, int sampleRate, double[] units)
    {
        var count = units.Length - 1;
        var levels = new double[count];
        for (var u = 0; u < count; u++)
        {
            var from = Math.Clamp((int)(units[u] * sampleRate), 0, mono.Length);
            var to = Math.Clamp((int)(units[u + 1] * sampleRate), from, mono.Length);
            double acc = 0;
            for (var i = from; i < to; i++) acc += mono[i] * (double)mono[i];
            levels[u] = 10 * Math.Log10(acc / Math.Max(1, to - from) + 1e-12);
        }

        return levels;
    }

    private static double[] Features(float[] mono, int sampleRate, double[] units, double[] levels)
    {
        var count = units.Length - 1;
        var bins = FftSize / 2;
        var binHz = sampleRate / (double)FftSize;
        var pitch = new int[bins];
        var band = new int[bins];
        for (var k = 0; k < bins; k++)
        {
            var hz = k * binHz;
            pitch[k] = hz < ChromaLowHz || hz > ChromaHighHz ? -1 : ((int)Math.Round(12 * Math.Log2(hz / 440.0)) % 12 + 12 + 9) % 12;
            band[k] = hz < BandLowHz || hz >= BandHighHz ? -1
                : Math.Min(Bands - 1, (int)(Bands * Math.Log(hz / BandLowHz) / Math.Log(BandHighHz / BandLowHz)));
        }

        var chroma = new double[count * Pitches];
        var power = new double[count * Bands];
        var fft = new Fft(FftSize);
        var window = Fft.Hann(FftSize);
        var re = new float[FftSize];
        var im = new float[FftSize];
        var unit = 0;
        var frames = mono.Length / Hop + 1;
        for (var f = 0; f < frames; f++)
        {
            var time = f * Hop / (double)sampleRate;
            if (time < units[0]) continue;
            while (unit < count && time >= units[unit + 1]) unit++;
            if (unit >= count) break;
            var start = f * Hop - FftSize / 2;
            for (var i = 0; i < FftSize; i++)
            {
                var index = start + i;
                re[i] = index >= 0 && index < mono.Length ? mono[index] * window[i] : 0f;
                im[i] = 0;
            }

            fft.Forward(re, im);
            for (var k = 1; k < bins; k++)
            {
                var energy = re[k] * (double)re[k] + im[k] * (double)im[k];
                if (band[k] >= 0) power[unit * Bands + band[k]] += energy;
                if (pitch[k] >= 0) chroma[unit * Pitches + pitch[k]] += Math.Sqrt(energy);
            }
        }

        var loudest = double.MinValue;
        foreach (var level in levels) loudest = Math.Max(loudest, level);
        var raw = new double[count * Dimensions];
        Span<double> shape = stackalloc double[Bands];
        for (var u = 0; u < count; u++)
        {
            double total = 0;
            for (var c = 0; c < Pitches; c++) total += chroma[u * Pitches + c];
            for (var c = 0; c < Pitches; c++)
                raw[u * Dimensions + c] = total > 1e-9 ? Math.Log(1 + 10 * chroma[u * Pitches + c] / total) : 0;

            var top = double.MinValue;
            for (var b = 0; b < Bands; b++)
            {
                shape[b] = 10 * Math.Log10(power[u * Bands + b] + 1e-12);
                top = Math.Max(top, shape[b]);
            }

            double mean = 0;
            for (var b = 0; b < Bands; b++)
            {
                shape[b] = Math.Max(shape[b], top - BandFloorDb);
                mean += shape[b];
            }

            mean /= Bands;
            for (var b = 0; b < Bands; b++) raw[u * Dimensions + Pitches + b] = shape[b] - mean;
            raw[u * Dimensions + Pitches + Bands] = Math.Max(levels[u], loudest - LevelFloorDb);
        }

        for (var d = 0; d < Dimensions; d++)
        {
            double mean = 0;
            for (var u = 0; u < count; u++) mean += raw[u * Dimensions + d];
            mean /= count;
            double variance = 0;
            for (var u = 0; u < count; u++)
            {
                var delta = raw[u * Dimensions + d] - mean;
                variance += delta * delta;
            }

            var floor = d < Pitches ? ChromaSpread : d < Pitches + Bands ? BandSpreadDb : LevelSpreadDb;
            var deviation = Math.Max(floor, Math.Sqrt(variance / count));
            var weight = d < Pitches ? 1 / Math.Sqrt(Pitches) : d < Pitches + Bands ? 1 / Math.Sqrt(Bands) : 1;
            for (var u = 0; u < count; u++)
                raw[u * Dimensions + d] = weight * (raw[u * Dimensions + d] - mean) / deviation;
        }

        return raw;
    }

    private static double[] Novelty(double[] features, int count)
    {
        var smooth = new double[count * Dimensions];
        var norms = new double[count];
        for (var u = 0; u < count; u++)
        {
            var from = Math.Max(0, u - SmoothUnits);
            var to = Math.Min(count, u + SmoothUnits + 1);
            double squares = Bias * Bias;
            for (var d = 0; d < Dimensions; d++)
            {
                double acc = 0;
                for (var k = from; k < to; k++) acc += features[k * Dimensions + d];
                acc /= to - from;
                smooth[u * Dimensions + d] = acc;
                squares += acc * acc;
            }

            norms[u] = Math.Sqrt(squares);
        }

        double Similarity(int a, int b)
        {
            var dot = Bias * Bias;
            for (var d = 0; d < Dimensions; d++) dot += smooth[a * Dimensions + d] * smooth[b * Dimensions + d];
            return dot / (norms[a] * norms[b]);
        }

        var taper = new double[KernelUnits];
        for (var k = 0; k < KernelUnits; k++) taper[k] = Math.Exp(-0.5 * Math.Pow((k + 0.5) / (0.5 * KernelUnits), 2));

        var novelty = new double[count];
        for (var i = MinimumSideUnits; i <= count - MinimumSideUnits; i++)
        {
            var before = Math.Min(KernelUnits, i);
            var after = Math.Min(KernelUnits, count - i);
            double within = 0, withinWeight = 0, across = 0, acrossWeight = 0;
            for (var a = 0; a < before; a++)
            {
                for (var b = a + 1; b < before; b++)
                {
                    var weight = taper[a] * taper[b];
                    within += weight * Similarity(i - 1 - a, i - 1 - b);
                    withinWeight += weight;
                }

                for (var b = 0; b < after; b++)
                {
                    var weight = taper[a] * taper[b];
                    across += weight * Similarity(i - 1 - a, i + b);
                    acrossWeight += weight;
                }
            }

            for (var a = 0; a < after; a++)
            {
                for (var b = a + 1; b < after; b++)
                {
                    var weight = taper[a] * taper[b];
                    within += weight * Similarity(i + a, i + b);
                    withinWeight += weight;
                }
            }

            if (withinWeight <= 0 || acrossWeight <= 0) continue;
            novelty[i] = Math.Max(0, within / withinWeight - across / acrossWeight);
        }

        return novelty;
    }

    private static List<int> Boundaries(double[] novelty, double[] features, double[] units, double[] downbeats, bool trustBars)
    {
        var count = novelty.Length;
        var peaks = new List<int>();
        for (var i = 1; i < count; i++)
        {
            if (novelty[i] < BoundaryNovelty) continue;
            var top = true;
            for (var k = Math.Max(1, i - MinimumSectionUnits / 2); k <= Math.Min(count - 1, i + MinimumSectionUnits / 2) && top; k++)
                if (novelty[k] > novelty[i] || novelty[k] == novelty[i] && k < i) top = false;
            if (top) peaks.Add(i);
        }

        peaks.Sort((x, y) => novelty[y].CompareTo(novelty[x]));
        var kept = new List<int>();
        foreach (var peak in peaks)
        {
            var index = Refine(peak, features, count);
            if (trustBars) index = Snap(index, units, downbeats);
            var clear = Far(units, 0, index) && Far(units, index, count);
            foreach (var other in kept)
                if (!Far(units, Math.Min(other, index), Math.Max(other, index))) clear = false;
            if (clear) kept.Add(index);
        }

        kept.Sort();
        return kept;
    }

    private static int Refine(int peak, double[] features, int count)
    {
        var best = peak;
        var bestStep = -1.0;
        for (var k = Math.Max(RefineSide, peak - RefineReach); k <= Math.Min(count - RefineSide, peak + RefineReach); k++)
        {
            double step = 0;
            for (var d = 0; d < Dimensions; d++)
            {
                double before = 0, after = 0;
                for (var u = 1; u <= RefineSide; u++)
                {
                    before += features[(k - u) * Dimensions + d];
                    after += features[(k + u - 1) * Dimensions + d];
                }

                step += (after - before) * (after - before);
            }

            if (step <= bestStep) continue;
            bestStep = step;
            best = k;
        }

        return best;
    }

    private static double Peak(double[] novelty, int index)
    {
        var peak = 0.0;
        for (var k = Math.Max(0, index - RefineReach - SnapUnits); k <= Math.Min(novelty.Length - 1, index + RefineReach + SnapUnits); k++)
            peak = Math.Max(peak, novelty[k]);
        return peak;
    }

    private static bool Far(double[] units, int from, int to)
    {
        return to - from >= MinimumSectionUnits && units[to] - units[from] >= MinimumSectionSeconds;
    }

    private static int Snap(int index, double[] units, double[] downbeats)
    {
        var best = -1;
        for (var k = Math.Max(1, index - SnapUnits); k <= Math.Min(units.Length - 2, index + SnapUnits); k++)
        {
            var position = Array.BinarySearch(downbeats, units[k]);
            if (position < 0)
            {
                position = ~position;
                var near = position < downbeats.Length && Math.Abs(downbeats[position] - units[k]) < 0.02
                           || position > 0 && Math.Abs(downbeats[position - 1] - units[k]) < 0.02;
                if (!near) continue;
            }

            if (best < 0 || Math.Abs(k - index) < Math.Abs(best - index)) best = k;
        }

        return best < 0 ? index : best;
    }

    private static TrackSection[] Sections(List<int> boundaries, double[] units, double[] levels, double[] features, double[] novelty)
    {
        var count = units.Length - 1;
        var edges = new List<int> { 0 };
        edges.AddRange(boundaries);
        edges.Add(count);

        double trackPower = 0, trackSeconds = 0;
        for (var u = 0; u < count; u++)
        {
            var seconds = units[u + 1] - units[u];
            trackPower += Math.Pow(10, levels[u] / 10) * seconds;
            trackSeconds += seconds;
        }

        var trackLevel = 10 * Math.Log10(trackPower / Math.Max(1e-9, trackSeconds) + 1e-12);
        double strongest = 0;
        foreach (var value in novelty) strongest = Math.Max(strongest, value);

        var sections = new TrackSection[edges.Count - 1];
        var centres = new double[sections.Length * Dimensions];
        for (var s = 0; s < sections.Length; s++)
        {
            var from = edges[s];
            var to = edges[s + 1];
            double power = 0, seconds = 0;
            for (var u = from; u < to; u++)
            {
                var length = units[u + 1] - units[u];
                power += Math.Pow(10, levels[u] / 10) * length;
                seconds += length;
                for (var d = 0; d < Dimensions; d++) centres[s * Dimensions + d] += features[u * Dimensions + d];
            }

            for (var d = 0; d < Dimensions; d++) centres[s * Dimensions + d] /= Math.Max(1, to - from);
            var level = 10 * Math.Log10(power / Math.Max(1e-9, seconds) + 1e-12) - trackLevel;
            sections[s] = new TrackSection
            {
                Start = Math.Round(units[from], 3),
                End = Math.Round(units[to], 3),
                Level = Math.Round(Math.Max(level, -LevelFloorDb), 1),
                Strength = s == 0 || strongest <= 0 ? 0 : Math.Round(Peak(novelty, from) / strongest, 3)
            };
        }

        for (var s = 0; s < sections.Length; s++)
        {
            for (var other = 0; other < s; other++)
            {
                if (Math.Abs(sections[s].Level - sections[other].Level) > RepeatLevelDb) continue;
                double dot = 0, first = 0, second = 0;
                for (var d = 0; d < Dimensions; d++)
                {
                    var x = centres[s * Dimensions + d];
                    var y = centres[other * Dimensions + d];
                    dot += x * y;
                    first += x * x;
                    second += y * y;
                }

                if (first < 1e-12 || second < 1e-12 || dot / Math.Sqrt(first * second) < RepeatSimilarity) continue;
                var group = sections[other].Repeat >= 0 ? sections[other].Repeat : other;
                sections[other].Repeat = group;
                sections[s].Repeat = group;
                break;
            }
        }

        return sections;
    }
}
