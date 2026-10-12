using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.Features.Playback.Analysis;
using HyPlayer.Features.Playback.Analysis.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public enum MixTechnique
{
    Crossfade,
    BeatMix,
    FilterCut,
    Gapless
}

public sealed class MixPlan
{
    public MixTechnique Technique { get; init; }
    public double AExit { get; init; }
    public double Overlap { get; init; }
    public double BEntry { get; init; }
    public double Rate { get; init; } = 1;
    public double RampBack { get; init; }
    public double Handoff { get; init; } = 0.5;
    public double GainB { get; init; } = 1;
    public int Bars { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double AFadeEnd => AExit + Overlap;
    public double BContentAtOverlapEnd => BEntry + Overlap * Rate;
}

public sealed class AutoMixPlannerOptions
{
    public double MaxOverlapSeconds { get; init; } = 40;
    public int PreferredBars { get; init; } = 16;
    public double CrossfadeSeconds { get; init; } = 6;
    public bool AllowTimeStretch { get; init; } = true;
    public double MaxTempoDeviation { get; init; } = 0.06;
    public bool AlbumContinuation { get; init; }
}

public static class AutoMixPlanner
{
    private const double LeadClashWeight = 0.5;

    public static MixPlan Plan(TrackAnalysis a, TrackAnalysis b,
        IReadOnlyList<(double Start, double End)> vocalsA,
        IReadOnlyList<(double Start, double End)> vocalsB,
        AutoMixPlannerOptions options,
        IReadOnlyList<(double Start, double End)>? leadsA = null,
        IReadOnlyList<(double Start, double End)>? leadsB = null)
    {
        leadsA ??= Array.Empty<(double Start, double End)>();
        leadsB ??= Array.Empty<(double Start, double End)>();
        var soundEndA = a.SoundEnd > 0 ? a.SoundEnd : a.Duration;
        var soundStartB = Math.Max(0, b.SoundStart);

        if (options.AlbumContinuation && a.Duration - soundEndA < 0.4 && soundStartB < 0.4)
            return new MixPlan { Technique = MixTechnique.Gapless, AExit = a.Duration, Overlap = 0, BEntry = 0, Reason = "album continuation" };

        var gainB = LoudnessMatch(a, b);
        var vocalEndA = LastVocalEnd(vocalsA, soundEndA);
        var beatOk = a.HasBeatGrid && b.HasBeatGrid && a.BeatConfidence >= 0.55 && b.BeatConfidence >= 0.55 &&
                     a.Downbeats.Length >= 8 && b.Downbeats.Length >= 8;
        if (!beatOk) return Crossfade(vocalsB, soundEndA, soundStartB, vocalEndA, gainB, options, "beat grid unreliable");

        var (rate, multiplier) = BestRate(a.Bpm, b.Bpm);
        var maxDeviation = options.AllowTimeStretch ? options.MaxTempoDeviation : 0.008;
        var technique = Math.Abs(rate - 1) <= maxDeviation ? MixTechnique.BeatMix : MixTechnique.FilterCut;
        if (technique == MixTechnique.FilterCut) rate = 1;
        if (!options.AllowTimeStretch) rate = 1;

        var beat = 60 / a.Bpm;
        var bar = 4 * beat;
        var outroBars = a.OutroStart > 0 ? (soundEndA - a.OutroStart) / bar : 8;
        var firstDownbeatB = b.Downbeats.FirstOrDefault(d => d >= soundStartB - 0.05);
        var barB = 4 * 60 / (b.Bpm * multiplier);
        var introBars = b.IntroEnd > firstDownbeatB ? (b.IntroEnd - firstDownbeatB) / barB : 8;
        var limit = Math.Min(options.PreferredBars, Math.Floor(options.MaxOverlapSeconds / bar));
        limit = Math.Min(limit, Math.Max(4, outroBars));
        limit = Math.Min(limit, Math.Max(4, introBars));
        var keyDistance = KeyEstimator.CamelotDistance(a.Key, b.Key);
        var keyClash = keyDistance > 2 && a.KeyConfidence > 0.03 && b.KeyConfidence > 0.03;
        if (keyClash || technique == MixTechnique.FilterCut) limit = Math.Min(limit, 8);
        var longest = SnapBars(limit);
        if (longest < 4 || longest * bar > options.MaxOverlapSeconds)
            return Crossfade(vocalsB, soundEndA, soundStartB, vocalEndA, gainB, options, "overlap too short for beat mix");

        var reason = new List<string> { $"bpm {a.Bpm:0.0}->{b.Bpm:0.0} x{multiplier:0.##}", $"rate {rate:0.000}" };
        if (keyClash) reason.Add($"key distance {keyDistance}");
        if (vocalEndA > 0) reason.Add($"vocals end {vocalEndA:0.0}s");
        var tolerance = Math.Min(beat, 0.8);

        MixPlan Build(double overlap, string span, double exit, double handoff)
        {
            var entryB = ChooseEntry(b, vocalsB, firstDownbeatB, overlap * rate, barB);
            if (entryB > firstDownbeatB + 0.05) reason.Add($"skip intro to {entryB:0.0}s");
            reason.Add(span);
            return new MixPlan
            {
                Technique = technique,
                AExit = exit,
                Overlap = overlap,
                BEntry = entryB,
                Rate = rate,
                RampBack = Math.Abs(rate - 1) > 0.0005 ? Math.Min(16 * barB, 20) : 0,
                Handoff = handoff,
                GainB = gainB,
                Bars = (int)Math.Round(overlap / bar),
                Reason = string.Join(", ", reason)
            };
        }

        (double Overlap, int Bars, double Exit, double Handoff)? crowded = null;
        if (technique == MixTechnique.BeatMix)
        {
            for (var bars = longest; bars >= 2; bars = bars > 4 ? bars / 2 : bars - 1)
            {
                var overlap = bars * bar;
                var exit = ChooseExit(a, soundEndA, overlap, double.MinValue);
                if (exit < 0 || vocalEndA + beat > exit + overlap)
                {
                    var covering = ChooseCoveringExit(a, overlap, vocalEndA + beat, a.Duration + bar);
                    if (covering >= 0) exit = covering;
                    else if (vocalEndA > exit + overlap + tolerance) exit = -1;
                }
                if (exit < 0) continue;
                var handoff = vocalEndA > exit ? Math.Clamp((vocalEndA - exit) / overlap, 0.5, 0.9) : 0.5;
                var bOverlap = overlap * rate;
                var entryB = ChooseEntry(b, vocalsB, firstDownbeatB, bOverlap, barB);
                var audible = overlap * (handoff + 0.7 * (1 - handoff));
                var clash = VocalClash(vocalsA, exit, exit + audible, vocalsB, entryB, entryB + bOverlap, rate);
                if (clash > 1) continue;
                if (crowded is { } kept && bars < 4) return Build(kept.Overlap, $"{kept.Bars} bars", kept.Exit, kept.Handoff);
                var busy = VocalClash(leadsA, exit, exit + audible, vocalsB, entryB, entryB + bOverlap, rate)
                           + VocalClash(vocalsA, exit, exit + audible, leadsB, entryB, entryB + bOverlap, rate)
                           + VocalClash(leadsA, exit, exit + audible, leadsB, entryB, entryB + bOverlap, rate);
                if (bars >= 4 && clash + LeadClashWeight * busy > 1)
                {
                    crowded ??= (overlap, bars, exit, handoff);
                    continue;
                }
                if (crowded is not null) reason.Add("mid-band busy");
                return Build(overlap, $"{bars} bars", exit, handoff);
            }
            if (crowded is { } longestCrowded)
                return Build(longestCrowded.Overlap, $"{longestCrowded.Bars} bars", longestCrowded.Exit, longestCrowded.Handoff);
        }

        var shortest = technique == MixTechnique.BeatMix ? 1 : 4;
        for (var bars = longest; bars >= shortest; bars = bars > 4 ? bars / 2 : bars - 1)
        {
            var exit = ChooseExit(a, soundEndA, bars * bar, vocalEndA - tolerance);
            if (exit < 0) continue;
            return Build(bars * bar, $"{bars} bars", exit, 0.5);
        }

        if (technique == MixTechnique.FilterCut)
        {
            var from = Math.Max(vocalEndA - tolerance, soundEndA - longest * bar);
            var exit = a.Beats.FirstOrDefault(t => t >= from, -1);
            var beats = exit < 0 ? 0 : (int)Math.Floor((soundEndA - exit) / beat);
            if (beats >= 2) return Build(beats * beat, $"{beats} beats", exit, 0.5);
        }

        return Crossfade(vocalsB, soundEndA, soundStartB, vocalEndA, gainB, options, "vocals run to the end");
    }

    private static MixPlan Crossfade(IReadOnlyList<(double Start, double End)> vocalsB, double soundEndA, double soundStartB,
        double vocalEndA, double gainB, AutoMixPlannerOptions options, string reason)
    {
        var overlap = Math.Clamp(options.CrossfadeSeconds, 2, 12);
        overlap = Math.Min(overlap, Math.Max(1, soundEndA - 1));
        var exit = Math.Max(0, soundEndA - overlap);
        var handoff = 0.0;
        if (vocalEndA > exit)
        {
            var lead = Math.Clamp(FirstVocalStart(vocalsB, soundStartB) - soundStartB, 0, 3);
            exit = Math.Max(exit, Math.Min(vocalEndA - lead, soundEndA - 1.2));
            overlap = soundEndA - exit;
            handoff = Math.Clamp((vocalEndA - exit) / overlap, 0, 0.8);
            reason += $", vocals end {vocalEndA:0.0}s";
        }
        return new MixPlan
        {
            Technique = MixTechnique.Crossfade,
            AExit = exit,
            Overlap = overlap,
            BEntry = soundStartB,
            Rate = 1,
            Handoff = handoff,
            GainB = gainB,
            Reason = reason
        };
    }

    private static double LastVocalEnd(IReadOnlyList<(double Start, double End)> vocals, double soundEnd)
    {
        var last = -1.0;
        foreach (var (start, end) in vocals)
            if (end - start >= 0.8 && start < soundEnd && end > soundEnd - 60 && end > last) last = end;
        return Math.Min(last, soundEnd);
    }

    private static double FirstVocalStart(IReadOnlyList<(double Start, double End)> vocals, double soundStart)
    {
        var first = double.MaxValue;
        foreach (var (start, end) in vocals)
            if (end - start >= 0.4 && start >= soundStart - 0.5 && start < first) first = start;
        return first;
    }

    private static (double rate, double multiplier) BestRate(double bpmA, double bpmB)
    {
        var best = (rate: bpmA / bpmB, multiplier: 1.0);
        foreach (var m in new[] { 2.0, 0.5 })
        {
            var r = bpmA / (bpmB * m);
            if (Math.Abs(r - 1) < Math.Abs(best.rate - 1)) best = (r, m);
        }
        return best;
    }

    private static int SnapBars(double bars)
    {
        foreach (var candidate in new[] { 32, 16, 8, 4 })
            if (bars >= candidate) return candidate;
        return 0;
    }

    private static double ChooseExit(TrackAnalysis a, double soundEndA, double overlap, double earliest)
    {
        var downbeats = a.Downbeats;
        var anchor = a.OutroStart > 0 ? Array.FindIndex(downbeats, d => d >= a.OutroStart - 0.05) : -1;
        var limit = soundEndA - overlap;
        var minimum = Math.Max(Math.Max(0, a.Duration - 45), earliest);
        var candidates = new List<int>();
        for (var i = downbeats.Length - 1; i >= 0; i--)
        {
            if (downbeats[i] > limit + 0.05) continue;
            if (downbeats[i] < minimum) break;
            candidates.Add(i);
        }
        if (candidates.Count == 0) return -1;
        var phase = anchor >= 0 ? anchor : 0;
        var phrase = candidates.FirstOrDefault(i => ((i - phase) % 4 + 4) % 4 == 0, -1);
        return downbeats[phrase >= 0 ? phrase : candidates[0]];
    }

    private static double ChooseCoveringExit(TrackAnalysis a, double overlap, double cover, double latestEnd)
    {
        var minimum = Math.Max(0, a.Duration - 45);
        foreach (var downbeat in a.Downbeats)
        {
            if (downbeat < minimum || downbeat + overlap < cover) continue;
            return downbeat + overlap <= latestEnd ? downbeat : -1;
        }
        return -1;
    }

    private static double ChooseEntry(TrackAnalysis b, IReadOnlyList<(double Start, double End)> vocalsB,
        double firstDownbeat, double bOverlap, double barB)
    {
        var firstVocal = FirstVocalStart(vocalsB, firstDownbeat);
        if (firstVocal == double.MaxValue) return firstDownbeat;
        if (firstVocal - firstDownbeat < bOverlap + 2 * barB) return firstDownbeat;
        var downbeats = b.Downbeats;
        var vocalBar = Array.FindLastIndex(downbeats, d => d <= firstVocal + barB * 0.25);
        if (vocalBar < 0) return firstDownbeat;
        var entryIndex = vocalBar - Math.Max(1, (int)Math.Round(bOverlap / barB));
        if (entryIndex < 0) return firstDownbeat;
        var entry = downbeats[entryIndex];
        if (entry <= firstDownbeat + barB || entry > b.Duration * 0.4) return firstDownbeat;
        return entry;
    }

    private static double VocalClash(IReadOnlyList<(double Start, double End)> vocalsA, double aStart, double aEnd,
        IReadOnlyList<(double Start, double End)> vocalsB, double bStart, double bEnd, double rate)
    {
        if (vocalsA.Count == 0 || vocalsB.Count == 0) return 0;
        double total = 0;
        foreach (var va in vocalsA)
        {
            var s1 = Math.Max(va.Start, aStart);
            var e1 = Math.Min(va.End, aEnd);
            if (e1 <= s1) continue;
            foreach (var vb in vocalsB)
            {
                var s2 = Math.Max(vb.Start, bStart);
                var e2 = Math.Min(vb.End, bEnd);
                if (e2 <= s2) continue;
                var outS2 = aStart + (s2 - bStart) / rate;
                var outE2 = aStart + (e2 - bStart) / rate;
                var overlap = Math.Min(e1, outE2) - Math.Max(s1, outS2);
                if (overlap > 0) total += overlap;
            }
        }
        return total;
    }

    private static double LoudnessMatch(TrackAnalysis a, TrackAnalysis b)
    {
        if (a.IntegratedLoudness <= -60 || b.IntegratedLoudness <= -60) return 1;
        var db = Math.Clamp(a.IntegratedLoudness - b.IntegratedLoudness, -6, 6);
        return Math.Pow(10, db / 20);
    }
}
