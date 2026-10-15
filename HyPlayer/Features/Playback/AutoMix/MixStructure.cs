using System;
using System.Collections.Generic;
using HyPlayer.Features.Playback.Analysis;
using HyPlayer.Features.Playback.Analysis.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public static class MixStructure
{
    public const double RunCoverage = 0.8;
    public const double BeatCoverage = 0.5;
    public const int FitBeats = 16;
    public const double TempoAgreement = 0.00025;
    public const double VocalFreeShare = 0.1;
    public const double EnergyDropDb = 3;

    public static TempoFit? Stable(TrackAnalysis track, double from, double to, bool extendForward)
    {
        var beats = track.Beats;
        if (beats.Length < TempoRuns.MinimumFitBeats || track.Bpm <= 0 || to <= from) return null;
        var beat = 60 / track.Bpm;
        var regionFrom = Math.Max(from, beats[0] - 0.5 * beat);
        var regionTo = Math.Min(to, beats[^1] + beat);
        if (regionTo - regionFrom < BeatCoverage * (to - from)) return null;
        if (track.TempoRuns is { } runs && Covered(runs, regionFrom, regionTo) < RunCoverage) return null;

        var span = FitBeats * beat;
        if (regionTo - regionFrom < span)
        {
            if (extendForward) regionTo = regionFrom + span;
            else regionFrom = regionTo - span;
        }

        var fit = TempoRuns.Fit(beats, regionFrom - 0.5 * beat, regionTo + 0.5 * beat);
        return fit is { } found && found.Deviation <= TempoRuns.ToleranceSeconds ? found : null;
    }

    public static double Covered(IReadOnlyList<TempoRun> runs, double from, double to)
    {
        if (to <= from) return 0;
        double covered = 0;
        foreach (var run in runs)
        {
            var slack = 0.5 * run.Period;
            var start = Math.Max(from, run.Start - slack);
            var end = Math.Min(to, run.End + slack);
            if (end > start) covered += end - start;
        }

        return Math.Min(1, covered / (to - from));
    }

    public static double LocalBpm(TrackAnalysis track, TempoFit fit, double from, double to)
    {
        var local = fit.Bpm;
        if (local <= 0) return track.Bpm;
        var tolerance = Math.Max(TempoAgreement, 3 * fit.Uncertainty / fit.Period);
        if (track.Bpm > 0 && Math.Abs(track.Bpm / local - 1) <= tolerance) return track.Bpm;
        var run = TempoRuns.Containing(track.TempoRuns, from, to);
        if (run is not null && Math.Abs(run.Bpm / local - 1) <= tolerance) return run.Bpm;
        return local;
    }

    public static double ExitBpm(TrackAnalysis track, double from)
    {
        if (track.TempoRuns is not { Length: > 0 } runs) return track.Bpm;
        for (var i = runs.Length - 1; i >= 0; i--)
            if (runs[i].End > from && runs[i].Bpm > 0) return Agreed(track, runs[i]);
        return track.Bpm;
    }

    public static double EntryBpm(TrackAnalysis track, double until)
    {
        if (track.TempoRuns is not { Length: > 0 } runs) return track.Bpm;
        foreach (var run in runs)
            if (run.Start < until && run.Bpm > 0) return Agreed(track, run);
        return track.Bpm;
    }

    private static double Agreed(TrackAnalysis track, TempoRun run)
    {
        return track.Bpm > 0 && Math.Abs(track.Bpm / run.Bpm - 1) <= TempoAgreement ? track.Bpm : run.Bpm;
    }

    public static double VocalShare(double from, double to, IReadOnlyList<(double Start, double End)> vocals)
    {
        if (to <= from) return 0;
        double sung = 0;
        foreach (var (start, end) in vocals)
        {
            var overlap = Math.Min(end, to) - Math.Max(start, from);
            if (overlap > 0) sung += overlap;
        }

        return Math.Min(1, sung / (to - from));
    }

    public static int SectionAt(TrackAnalysis track, double time, double tolerance)
    {
        if (track.Sections is not { Length: > 0 } sections) return -1;
        var best = -1;
        for (var i = 0; i < sections.Length; i++)
        {
            var distance = Math.Abs(sections[i].Start - time);
            if (distance <= tolerance && (best < 0 || distance < Math.Abs(sections[best].Start - time))) best = i;
        }

        return best;
    }

    public static bool IsQuieter(TrackSection[] sections, int index)
    {
        return sections[index].IsLow || index > 0 && sections[index].Level <= sections[index - 1].Level - EnergyDropDb;
    }

    public static double NearestDownbeat(TrackAnalysis track, double time, double tolerance)
    {
        var best = -1.0;
        foreach (var downbeat in track.Downbeats)
        {
            if (Math.Abs(downbeat - time) > tolerance) continue;
            if (best < 0 || Math.Abs(downbeat - time) < Math.Abs(best - time)) best = downbeat;
        }

        return best;
    }
}
