using System;
using System.Collections.Generic;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public readonly record struct TempoFit(double Period, double Phase, int Beats, double Deviation, double Uncertainty)
{
    public double Bpm => Period > 0 ? 60 / Period : 0;

    public double Snap(double time)
    {
        return Period > 0 ? Phase + Math.Round((time - Phase) / Period) * Period : time;
    }
}

public static class TempoRuns
{
    public const double ToleranceSeconds = 0.025;
    public const double OutlierSeconds = 0.05;
    public const int MinimumBeats = 32;
    public const int MinimumFitBeats = 8;
    public const double SameTempo = 0.005;
    private const int PieceBeats = 16;
    private const int EvidenceBeats = 8;
    private const int EvidenceHop = 2;
    private const int SupportedBeats = 8;
    private const double EvidenceReachSeconds = 0.035;
    private const double EvidenceRatio = 1.35;
    private const double EvidenceStep = 0.01;
    private const int EvidenceSteps = 15;
    private const double QuietShare = 0.15;
    private const double EventShare = 0.3;
    private const int MinimumEvents = 3;
    private const int SeedBeats = 8;
    private const int SmoothBeats = 8;
    private const int OutlierSpacing = 8;
    private const int MaximumStep = 2;
    private const double SeedSpread = 0.2;

    private enum BeatEvidence
    {
        None,
        For,
        Against
    }

    private sealed class Line
    {
        private double _n, _sk, _st, _skk, _skt;

        public double Period { get; private set; }
        public double Phase { get; private set; }

        public void Add(double index, double time, double sign = 1)
        {
            _n += sign;
            _sk += sign * index;
            _st += sign * time;
            _skk += sign * index * index;
            _skt += sign * index * time;
            var spread = _n * _skk - _sk * _sk;
            if (_n < 2 || spread <= 0) return;
            Period = (_n * _skt - _sk * _st) / spread;
            Phase = (_st - Period * _sk) / _n;
        }

        public double At(double index) => Phase + Period * index;
    }

    private sealed class Growth
    {
        public readonly List<int> Members = new();
        public readonly List<int> Indices = new();
        public readonly Line Line = new();
    }

    public static TempoRun[] Find(double[] beats)
    {
        return Search(beats, null);
    }

    public static TempoRun[] Find(double[] beats, SpectralFeatures features)
    {
        return Search(beats, Evidence(beats, features));
    }

    public static double Opposed(double[] beats, SpectralFeatures features)
    {
        if (beats.Length == 0) return 0;
        var opposed = 0;
        foreach (var kind in Evidence(beats, features))
            if (kind == BeatEvidence.Against) opposed++;
        return opposed / (double)beats.Length;
    }

    private static TempoRun[] Search(double[] beats, BeatEvidence[]? evidence)
    {
        var runs = new List<Growth>();
        var from = 0;
        var floor = 0;
        while (from + SeedBeats <= beats.Length)
        {
            if (evidence is not null && evidence[from] == BeatEvidence.Against)
            {
                from++;
                floor = from;
                continue;
            }

            var ceiling = beats.Length;
            if (evidence is not null)
            {
                ceiling = from;
                while (ceiling < beats.Length && evidence[ceiling] != BeatEvidence.Against) ceiling++;
            }

            var growth = Grow(beats, from, floor, ceiling);
            if (growth is null)
            {
                from++;
                continue;
            }

            runs.Add(growth);
            from = floor = growth.Members[^1] + 1;
        }

        for (var i = 1; i < runs.Count; i++) Rebalance(beats, runs[i - 1], runs[i]);
        for (var i = 1; i < runs.Count; i++)
        {
            if (!Join(beats, runs[i - 1], runs[i])) continue;
            runs.RemoveAt(i);
            i--;
        }

        var result = new List<TempoRun>(runs.Count);
        foreach (var run in runs)
        {
            if (run.Members.Count < MinimumBeats || run.Line.Period <= 0) continue;
            if (evidence is not null)
            {
                var supported = 0;
                foreach (var member in run.Members)
                    if (evidence[member] == BeatEvidence.For) supported++;
                if (supported < SupportedBeats) continue;
            }

            result.Add(new TempoRun
            {
                Start = Math.Round(beats[run.Members[0]], 4),
                End = Math.Round(beats[run.Members[^1]], 4),
                Period = Math.Round(run.Line.Period, 6),
                Phase = Math.Round(run.Line.At(run.Indices[0]), 4),
                Beats = run.Indices[^1] - run.Indices[0] + 1,
                Deviation = Math.Round(Deviation(beats, run), 4)
            });
        }

        return result.ToArray();
    }

    private static BeatEvidence[] Evidence(double[] beats, SpectralFeatures features)
    {
        var evidence = new BeatEvidence[beats.Length];
        if (beats.Length < EvidenceBeats || features.Frames < 3) return evidence;
        var hop = features.HopSeconds;
        var onset = new double[features.Frames];
        for (var i = 0; i < onset.Length; i++)
        {
            var before = features.Onset[Math.Max(0, i - 1)];
            var after = features.Onset[Math.Min(onset.Length - 1, i + 1)];
            onset[i] = 0.25 * before + 0.5 * features.Onset[i] + 0.25 * after;
        }

        double At(double time)
        {
            var frame = (int)Math.Round(time / hop);
            return frame >= 0 && frame < onset.Length ? onset[frame] : 0;
        }

        var windows = (beats.Length - EvidenceBeats) / EvidenceHop + 1;
        var kinds = new BeatEvidence[windows];
        var strengths = new double[windows];
        Span<double> ibis = stackalloc double[EvidenceBeats - 1];
        var reach = (int)Math.Round(EvidenceReachSeconds / hop);
        for (var w = 0; w < windows; w++)
        {
            var first = w * EvidenceHop;
            var last = first + EvidenceBeats - 1;
            for (var i = 0; i < EvidenceBeats - 1; i++) ibis[i] = beats[first + i + 1] - beats[first + i];
            ibis.Sort();
            var period = ibis[(EvidenceBeats - 1) / 2];
            var from = beats[first] - 0.5 * period;
            var to = beats[last] + 0.5 * period;

            double top = 0;
            var firstFrame = Math.Max(1, (int)(from / hop));
            var lastFrame = Math.Min(onset.Length - 1, (int)(to / hop));
            for (var f = firstFrame; f < lastFrame; f++) top = Math.Max(top, onset[f]);
            strengths[w] = top;
            var events = 0;
            for (var f = firstFrame; f < lastFrame; f++)
                if (onset[f] >= EventShare * top && onset[f] > onset[f - 1] && onset[f] >= onset[f + 1]) events++;

            double grid = 0;
            foreach (var half in new[] { 0.0, 0.5 })
            {
                for (var d = -reach; d <= reach; d++)
                {
                    double sum = 0;
                    for (var i = first; i <= last; i++) sum += At(beats[i] + half * period + d * hop);
                    grid = Math.Max(grid, sum);
                }
            }

            double other = 0;
            for (var step = -EvidenceSteps; step <= EvidenceSteps; step++)
            {
                var candidate = period * (1 + step * EvidenceStep);
                var count = (int)Math.Floor((to - from) / candidate);
                if (count < 2) continue;
                var scale = EvidenceBeats / (double)count;
                for (var phase = 0.0; phase < candidate; phase += hop)
                {
                    double sum = 0;
                    for (var k = 0; k < count; k++) sum += At(from + phase + k * candidate);
                    other = Math.Max(other, sum * scale);
                }
            }

            kinds[w] = grid > 0 && other <= EvidenceRatio * grid ? BeatEvidence.For
                : events >= MinimumEvents ? BeatEvidence.Against
                : BeatEvidence.None;
        }

        var sorted = (double[])strengths.Clone();
        Array.Sort(sorted);
        var quiet = QuietShare * sorted[sorted.Length / 2];
        for (var w = 0; w < windows; w++)
            if (kinds[w] == BeatEvidence.Against && strengths[w] < quiet) kinds[w] = BeatEvidence.None;

        for (var i = 0; i < beats.Length; i++)
        {
            int supporting = 0, opposing = 0, covering = 0;
            var latest = Math.Min(windows - 1, i / EvidenceHop);
            for (var w = latest; w >= 0 && w * EvidenceHop + EvidenceBeats > i; w--)
            {
                covering++;
                if (kinds[w] == BeatEvidence.For) supporting++;
                else if (kinds[w] == BeatEvidence.Against) opposing++;
            }

            evidence[i] = covering == 0 ? BeatEvidence.None
                : 2 * supporting >= covering ? BeatEvidence.For
                : 2 * opposing > covering ? BeatEvidence.Against
                : BeatEvidence.None;
        }

        return evidence;
    }

    public static TempoRun? Containing(IReadOnlyList<TempoRun>? runs, double from, double to)
    {
        if (runs is null) return null;
        foreach (var run in runs)
            if (run.Covers(from, to)) return run;
        return null;
    }

    public static TempoFit? Fit(double[] beats, double from, double to)
    {
        var first = Array.FindIndex(beats, t => t >= from - 1e-6);
        if (first < 0) return null;
        var last = first;
        while (last + 1 < beats.Length && beats[last + 1] <= to + 1e-6) last++;
        if (last - first + 1 < MinimumFitBeats) return null;

        var ibis = new double[last - first];
        for (var i = first; i < last; i++) ibis[i - first] = beats[i + 1] - beats[i];
        Array.Sort(ibis);
        var typical = ibis[ibis.Length / 2];
        if (typical <= 0) return null;

        var line = new Line();
        var times = new List<double>(last - first + 1);
        var indices = new List<int>(last - first + 1);
        var index = 0;
        for (var i = first; i <= last; i++)
        {
            if (i > first)
            {
                var step = (int)Math.Round((beats[i] - beats[i - 1]) / typical);
                if (step < 1 || step > MaximumStep) return null;
                index += step;
            }

            times.Add(beats[i]);
            indices.Add(index);
            line.Add(index, beats[i]);
        }

        if (line.Period <= 0) return null;
        var residuals = new double[times.Count];
        double squares = 0;
        for (var i = 0; i < residuals.Length; i++)
        {
            residuals[i] = times[i] - line.At(indices[i]);
            squares += residuals[i] * residuals[i];
        }

        var deviation = Smoothed(residuals);
        var count = residuals.Length;
        var span = (double)count * count * count - count;
        var uncertainty = Math.Sqrt(squares / Math.Max(1, count - 2)) * Math.Sqrt(12 / span);
        return new TempoFit(line.Period, line.At(0), count, deviation, uncertainty);
    }

    private static Growth? Grow(double[] beats, int from, int floor, int ceiling)
    {
        if (from + SeedBeats > ceiling) return null;
        Span<double> ibis = stackalloc double[SeedBeats - 1];
        for (var i = 0; i < SeedBeats - 1; i++) ibis[i] = beats[from + i + 1] - beats[from + i];
        ibis.Sort();
        var typical = ibis[(SeedBeats - 1) / 2];
        if (typical <= 0 || ibis[^1] - ibis[0] > SeedSpread * typical + 2 * OutlierSeconds) return null;

        var growth = new Growth();
        for (var i = 0; i < SeedBeats; i++) Append(growth, beats, from + i, i);
        if (!Holds(beats, growth)) return null;

        var sinceOutlier = OutlierSpacing;
        for (var next = from + SeedBeats; next < ceiling; next++)
        {
            var previous = growth.Members[^1];
            var period = growth.Line.Period;
            var step = (int)Math.Round((beats[next] - beats[previous]) / period);
            var fits = step >= 1 && step <= MaximumStep
                       && Math.Abs(beats[next] - growth.Line.At(growth.Indices[^1] + step)) <= OutlierSeconds;
            if (fits)
            {
                Append(growth, beats, next, growth.Indices[^1] + step);
                if (Holds(beats, growth))
                {
                    sinceOutlier++;
                    continue;
                }

                RemoveLast(growth, beats);
                break;
            }

            if (sinceOutlier < OutlierSpacing || next + 1 >= ceiling) break;
            var after = (int)Math.Round((beats[next + 1] - beats[previous]) / period);
            if (after < 1 || after > MaximumStep
                || Math.Abs(beats[next + 1] - growth.Line.At(growth.Indices[^1] + after)) > ToleranceSeconds) break;
            sinceOutlier = 0;
        }

        if (growth.Members.Count < PieceBeats) return null;
        for (var previous = from - 1; previous >= floor; previous--)
        {
            var step = (int)Math.Round((beats[growth.Members[0]] - beats[previous]) / growth.Line.Period);
            if (step < 1 || step > MaximumStep
                || Math.Abs(beats[previous] - growth.Line.At(growth.Indices[0] - step)) > ToleranceSeconds) break;
            Prepend(growth, beats, previous, growth.Indices[0] - step);
            if (Holds(beats, growth)) continue;
            RemoveFirst(growth, beats);
            break;
        }

        return growth;
    }

    private static void Append(Growth growth, double[] beats, int member, int index)
    {
        growth.Members.Add(member);
        growth.Indices.Add(index);
        growth.Line.Add(index, beats[member]);
    }

    private static void Prepend(Growth growth, double[] beats, int member, int index)
    {
        growth.Members.Insert(0, member);
        growth.Indices.Insert(0, index);
        growth.Line.Add(index, beats[member]);
    }

    private static void RemoveLast(Growth growth, double[] beats)
    {
        growth.Line.Add(growth.Indices[^1], beats[growth.Members[^1]], -1);
        growth.Members.RemoveAt(growth.Members.Count - 1);
        growth.Indices.RemoveAt(growth.Indices.Count - 1);
    }

    private static void RemoveFirst(Growth growth, double[] beats)
    {
        growth.Line.Add(growth.Indices[0], beats[growth.Members[0]], -1);
        growth.Members.RemoveAt(0);
        growth.Indices.RemoveAt(0);
    }

    private static bool Holds(double[] beats, Growth growth)
    {
        return Deviation(beats, growth) <= ToleranceSeconds;
    }

    private static double Deviation(double[] beats, Growth growth)
    {
        var residuals = new double[growth.Members.Count];
        for (var i = 0; i < residuals.Length; i++)
            residuals[i] = beats[growth.Members[i]] - growth.Line.At(growth.Indices[i]);
        return Smoothed(residuals);
    }

    private static double Smoothed(double[] residuals)
    {
        var worst = 0.0;
        if (residuals.Length < SmoothBeats)
        {
            foreach (var residual in residuals) worst = Math.Max(worst, Math.Abs(residual));
            return worst;
        }

        double sum = 0;
        for (var i = 0; i < residuals.Length; i++)
        {
            sum += residuals[i];
            if (i >= SmoothBeats) sum -= residuals[i - SmoothBeats];
            if (i >= SmoothBeats - 1) worst = Math.Max(worst, Math.Abs(sum / SmoothBeats));
        }

        return worst;
    }

    private static void Rebalance(double[] beats, Growth earlier, Growth later)
    {
        if (later.Members[0] - earlier.Members[^1] > 1) return;
        while (earlier.Members.Count > PieceBeats)
        {
            var member = earlier.Members[^1];
            var step = (int)Math.Round((beats[later.Members[0]] - beats[member]) / later.Line.Period);
            if (step < 1 || step > MaximumStep) break;
            var index = later.Indices[0] - step;
            var inEarlier = Math.Abs(beats[member] - earlier.Line.At(earlier.Indices[^1]));
            var inLater = Math.Abs(beats[member] - later.Line.At(index));
            if (inLater >= inEarlier || inLater > ToleranceSeconds) break;
            RemoveLast(earlier, beats);
            Prepend(later, beats, member, index);
        }
    }

    private static bool Join(double[] beats, Growth earlier, Growth later)
    {
        if (later.Members[0] - earlier.Members[^1] > MaximumStep) return false;
        var period = earlier.Members.Count >= later.Members.Count ? earlier.Line.Period : later.Line.Period;
        if (Math.Abs(earlier.Line.Period - later.Line.Period) > SameTempo * period) return false;
        var step = (int)Math.Round((beats[later.Members[0]] - beats[earlier.Members[^1]]) / period);
        if (step < 1 || step > MaximumStep) return false;
        var seam = earlier.Indices[^1] + 0.5 * step;
        var offset = later.Indices[0] - earlier.Indices[^1] - step;
        if (Math.Abs(earlier.Line.At(seam) - later.Line.At(seam + offset)) > ToleranceSeconds) return false;
        var shift = earlier.Indices[^1] + step - later.Indices[0];
        for (var i = 0; i < later.Members.Count; i++) Append(earlier, beats, later.Members[i], later.Indices[i] + shift);
        return true;
    }
}
