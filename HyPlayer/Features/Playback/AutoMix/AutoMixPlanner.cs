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

public enum MixTail
{
    None,
    Reverb,
    Echo
}

public readonly record struct MixRatePoint(double Position, double RateA, double RateB);

public sealed record MixPlan
{
    public MixTechnique Technique { get; init; }
    public double AExit { get; init; }
    public double Overlap { get; init; }
    public double BEntry { get; init; }
    public double Rate { get; init; } = 1;
    public double RampBack { get; init; }
    public double Handoff { get; init; } = 0.5;
    public double GainB { get; init; } = 1;
    public double GainRelease { get; init; } = MixLevels.BaseReleaseSeconds;
    public MixLoudness? Loudness { get; init; }
    public MixEnding? Ending { get; init; }
    public double EntryFade { get; init; }
    public MixTail Tail { get; init; }
    public double TailDelay { get; init; }
    public double TailOpen { get; init; }
    public double TailFull { get; init; }
    public double TailBloom { get; init; }
    public double TailLength { get; init; }
    public int Bars { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double TempoA { get; init; }
    public double TempoB { get; init; }
    public IReadOnlyList<MixRatePoint>? Glide { get; init; }
    public bool HasGlide => Glide is { Count: > 0 };
    public double AFadeEnd => AExit + Overlap;
    public double BContentAtOverlapEnd => BEntry + Overlap * Rate;
    public double Settle => Math.Max(Math.Max(RampBack, GainRelease), Tail == MixTail.None ? 0 : TailLength);
}

public sealed class AutoMixPlannerOptions
{
    public double MaxOverlapSeconds { get; init; } = 40;
    public int PreferredBars { get; init; } = 16;
    public double CrossfadeSeconds { get; init; } = 6;
    public bool AllowTimeStretch { get; init; } = true;
    public bool AllowGlide { get; init; }
    public double MaxTempoDeviation { get; init; } = 0.06;
    public bool AlbumContinuation { get; init; }
    public double PlaybackOffsetDb { get; init; }
    public bool TailEffects { get; init; } = true;
    public double EarliestExit { get; init; }
    public MixHistory? History { get; init; } = MixHistory.Shared;
    public bool RecordPlanned { get; init; } = true;
    public Action<string>? Log { get; init; }
}

public sealed class MixHistory
{
    private readonly object _gate = new();
    private string _pair = string.Empty;
    private MixTechnique? _technique;
    private MixTechnique? _before;

    public static MixHistory Shared { get; } = new();

    public MixTechnique? PreviousFor(string outgoing, string incoming)
    {
        lock (_gate) return Pair(outgoing, incoming) == _pair ? _before : _technique;
    }

    public void Record(string outgoing, string incoming, MixTechnique technique)
    {
        lock (_gate)
        {
            var pair = Pair(outgoing, incoming);
            if (pair != _pair)
            {
                _before = _technique;
                _pair = pair;
            }

            _technique = technique;
        }
    }

    private static string Pair(string outgoing, string incoming) => outgoing + "|" + incoming;
}

public sealed record MixCandidate(MixTemplate Template, string Anchor, MixPlan Plan, MixScore Score, bool Crowded = false, string? Rejected = null)
{
    public double Clash { get; init; }

    internal IReadOnlyList<string> Head { get; init; } = Array.Empty<string>();
    internal IReadOnlyList<string> Tail { get; init; } = Array.Empty<string>();

    public string Describe() => FormattableString.Invariant(
        $"{Template.Name} [{Anchor}] {Plan.Bars} bars exit A {Plan.AExit:0.00}s overlap {Plan.Overlap:0.00}s entry B {Plan.BEntry:0.00}s rate {Plan.Rate:0.0000}{(Plan.HasGlide ? " glide" : string.Empty)} handoff {Plan.Handoff:0.00}{(Crowded ? " crowded" : string.Empty)}");
}

public sealed class MixDecision
{
    public const int ExplainedCandidates = 5;

    public required MixPlan Plan { get; init; }
    public required IReadOnlyList<MixCandidate> Candidates { get; init; }
    public required IReadOnlyList<MixCandidate> Rejected { get; init; }
    public required int Chosen { get; init; }
    public int Choices { get; init; } = 1;
    public MixTechnique? Previous { get; init; }

    public IReadOnlyList<string> Explain()
    {
        var lines = new List<string>
        {
            FormattableString.Invariant(
                $"plan candidates: {Candidates.Count} accepted, {Rejected.Count} rejected, previous {(Previous is { } previous ? previous.ToString() : "none")}, chose #{Chosen + 1} of {Choices} within {MixScoring.VarietyMargin:0.0} of the best")
        };
        for (var i = 0; i < Candidates.Count; i++)
        {
            if (i >= ExplainedCandidates && i != Chosen) continue;
            lines.Add($"plan candidate #{i + 1}{(i == Chosen ? " (chosen)" : string.Empty)}: {Candidates[i].Describe()} score {Candidates[i].Score.Describe()}");
        }

        if (Rejected.Count > 0)
        {
            var reasons = new List<string>();
            foreach (var group in Rejected.GroupBy(candidate => candidate.Rejected))
                reasons.Add($"{group.Key} x{group.Count()}");
            lines.Add("plan rejected: " + string.Join(", ", reasons));
        }

        return lines;
    }
}

public static class AutoMixPlanner
{
    private const double LeadClashWeight = 0.5;
    private const double MinimumPlayOutOverlap = 0.3;
    private const double MinimumEntryFade = 1;
    private const double MaximumEntryFade = 4;
    private const double TrustedBeatConfidence = 0.55;
    private const double TrustedBarConfidence = 0.3;
    private const double BarLineTolerance = 0.12;
    private const double ReverbEntryFade = 2;
    private const double ReverbTailLength = 3.5;
    private const double ReverbOpenSeconds = 1.5;
    private const double ReverbFullSeconds = 0.3;
    private const double RefineFloor = 0.001;
    private const double ExitWindowSeconds = 45;
    private const double MaximumVocalClash = 1;
    private const double AvoidableVocalClash = 0.25;
    private const string NoStableTempo = "no stable tempo";
    private const string ExitPassed = "exit already passed";
    private const string StillSinging = "A still singing at the end of the overlap";

    public static MixPlan Plan(TrackAnalysis a, TrackAnalysis b,
        IReadOnlyList<(double Start, double End)> vocalsA,
        IReadOnlyList<(double Start, double End)> vocalsB,
        AutoMixPlannerOptions options,
        IReadOnlyList<(double Start, double End)>? leadsA = null,
        IReadOnlyList<(double Start, double End)>? leadsB = null)
    {
        var identified = a.TrackKey.Length > 0 && b.TrackKey.Length > 0;
        var history = identified ? options.History : null;
        var decision = Decide(a, b, vocalsA, vocalsB, options, leadsA, leadsB, history?.PreviousFor(a.TrackKey, b.TrackKey));
        if (options.RecordPlanned) history?.Record(a.TrackKey, b.TrackKey, decision.Plan.Technique);
        var log = options.Log ?? (identified ? AutoMixLog.Write : null);
        if (log is not null)
            foreach (var line in decision.Explain()) log(line);
        return decision.Plan;
    }

    public static MixDecision Decide(TrackAnalysis a, TrackAnalysis b,
        IReadOnlyList<(double Start, double End)> vocalsA,
        IReadOnlyList<(double Start, double End)> vocalsB,
        AutoMixPlannerOptions options,
        IReadOnlyList<(double Start, double End)>? leadsA = null,
        IReadOnlyList<(double Start, double End)>? leadsB = null,
        MixTechnique? previous = null)
    {
        var scene = new Scene(a, b, vocalsA, vocalsB, leadsA ?? Array.Empty<(double Start, double End)>(),
            leadsB ?? Array.Empty<(double Start, double End)>(), options, previous);

        if (options.AlbumContinuation && a.Duration - scene.SoundEndA < 0.4 && scene.SoundStartB < 0.4)
        {
            var gapless = new MixPlan { Technique = MixTechnique.Gapless, AExit = a.Duration, Overlap = 0, BEntry = 0, Reason = "album continuation" };
            var only = new MixCandidate(MixScoring.Template(MixShape.Gapless), "album", gapless, default);
            return new MixDecision { Plan = gapless, Candidates = (MixCandidate[])[only], Rejected = [], Chosen = 0, Previous = previous };
        }

        var fadeReason = Blends(scene);
        scene.Add(Fade(scene, fadeReason));

        var least = scene.Accepted.Min(candidate => candidate.Clash);
        foreach (var candidate in scene.Accepted)
            if (candidate.Clash > least + AvoidableVocalClash) scene.Rejected.Add(candidate with { Rejected = "avoidable vocal clash" });
        scene.Accepted.RemoveAll(candidate => candidate.Clash > least + AvoidableVocalClash);

        var ordered = scene.Accepted.OrderByDescending(candidate => candidate.Score.Total).ToList();
        var best = ordered[0].Score.Total;
        var choices = 1;
        var chosen = 0;
        if (a.TrackKey.Length > 0 && b.TrackKey.Length > 0)
        {
            while (choices < ordered.Count && ordered[choices].Score.Total >= best - MixScoring.VarietyMargin) choices++;
            chosen = (int)(Seed(a.TrackKey, b.TrackKey) % (uint)choices);
        }

        var pick = ordered[chosen];
        var parts = new List<string>(pick.Head);
        if (pick.Template.Shape == MixShape.BeatBlend && !pick.Crowded
            && ordered.Any(other => other.Template.Shape == MixShape.BeatBlend && other.Crowded && other.Plan.Bars > pick.Plan.Bars))
            parts.Add("mid-band busy");
        parts.AddRange(pick.Tail);
        var plan = parts.Count > 0 ? pick.Plan with { Reason = string.Join(", ", parts) } : pick.Plan;
        ordered[chosen] = pick with { Plan = plan };
        return new MixDecision
        {
            Plan = plan,
            Candidates = ordered,
            Rejected = scene.Rejected,
            Chosen = chosen,
            Choices = choices,
            Previous = previous
        };
    }

    public static uint Seed(string outgoing, string incoming)
    {
        var hash = 2166136261u;
        foreach (var c in outgoing)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        hash ^= 10;
        hash *= 16777619u;
        foreach (var c in incoming)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return hash;
    }

    private sealed class Scene
    {
        public Scene(TrackAnalysis a, TrackAnalysis b,
            IReadOnlyList<(double Start, double End)> vocalsA, IReadOnlyList<(double Start, double End)> vocalsB,
            IReadOnlyList<(double Start, double End)> leadsA, IReadOnlyList<(double Start, double End)> leadsB,
            AutoMixPlannerOptions options, MixTechnique? previous)
        {
            A = a;
            B = b;
            VocalsA = vocalsA;
            VocalsB = vocalsB;
            LeadsA = leadsA;
            LeadsB = leadsB;
            Options = options;
            Previous = previous;
            SoundEndA = a.SoundEnd > 0 ? a.SoundEnd : a.Duration;
            SoundStartB = Math.Max(0, b.SoundStart);
            VocalEndA = LastVocalEnd(vocalsA, SoundEndA);
        }

        public TrackAnalysis A { get; }
        public TrackAnalysis B { get; }
        public IReadOnlyList<(double Start, double End)> VocalsA { get; }
        public IReadOnlyList<(double Start, double End)> VocalsB { get; }
        public IReadOnlyList<(double Start, double End)> LeadsA { get; }
        public IReadOnlyList<(double Start, double End)> LeadsB { get; }
        public AutoMixPlannerOptions Options { get; }
        public MixTechnique? Previous { get; }
        public double SoundEndA { get; }
        public double SoundStartB { get; }
        public double VocalEndA { get; }
        public List<MixCandidate> Accepted { get; } = new();
        public List<MixCandidate> Rejected { get; } = new();

        public void Add(MixCandidate candidate)
        {
            foreach (var other in Accepted)
            {
                var x = other.Plan;
                var y = candidate.Plan;
                if (x.Technique == y.Technique && x.AExit == y.AExit && x.Overlap == y.Overlap && x.BEntry == y.BEntry
                    && x.Rate == y.Rate && x.Handoff == y.Handoff && x.Tail == y.Tail && x.EntryFade == y.EntryFade) return;
            }

            Accepted.Add(candidate);
        }

        public void Reject(MixTemplate template, string anchor, int bars, double exit, double overlap, double entry, string reason)
        {
            var plan = new MixPlan { Technique = template.Technique, AExit = exit, Overlap = overlap, BEntry = entry, Bars = bars };
            Rejected.Add(new MixCandidate(template, anchor, plan, default, false, reason));
        }
    }

    private readonly record struct Pairing(double Beat, double Bar, double BarB, double Tolerance, double MaxDeviation, double EntryBpm);

    private static string Blends(Scene scene)
    {
        var a = scene.A;
        var b = scene.B;
        var options = scene.Options;
        var beatOk = a.HasBeatGrid && b.HasBeatGrid && a.BeatConfidence >= TrustedBeatConfidence && b.BeatConfidence >= TrustedBeatConfidence &&
                     a.Downbeats.Length >= 8 && b.Downbeats.Length >= 8;
        if (!beatOk) return "beat grid unreliable";

        var exitBpm = MixStructure.ExitBpm(a, scene.SoundEndA - ExitWindowSeconds);
        var entryBpm = MixStructure.EntryBpm(b, b.Duration * 0.4);
        var (rate, multiplier) = BestRate(exitBpm, entryBpm);
        var maxDeviation = options.AllowTimeStretch ? options.MaxTempoDeviation : 0.008;
        var synced = Math.Abs(rate - 1) <= maxDeviation;

        var beat = 60 / exitBpm;
        var bar = 4 * beat;
        var outroBars = a.OutroStart > 0 ? (scene.SoundEndA - a.OutroStart) / bar : 8;
        var firstDownbeatB = b.Downbeats.FirstOrDefault(d => d >= scene.SoundStartB - 0.05);
        var barB = 4 * 60 / (entryBpm * multiplier);
        var introBars = b.IntroEnd > firstDownbeatB ? (b.IntroEnd - firstDownbeatB) / barB : 8;
        var limit = Math.Min(options.PreferredBars, Math.Floor(options.MaxOverlapSeconds / bar));
        limit = Math.Min(limit, Math.Max(4, outroBars));
        limit = Math.Min(limit, Math.Max(4, introBars));
        var longestBeat = synced ? SnapBars(limit) : 0;
        var longestFilter = SnapBars(Math.Min(limit, 8));
        var longest = synced ? longestBeat : longestFilter;
        if (longest < 4 || longest * bar > options.MaxOverlapSeconds) return "overlap too short for beat mix";

        var tolerance = Math.Min(beat, 0.8);
        var pairing = new Pairing(beat, bar, barB, tolerance, maxDeviation, entryBpm);
        var beatBlend = MixScoring.Template(MixShape.BeatBlend);
        var filterBlend = MixScoring.Template(MixShape.FilterBlend);

        if (synced)
        {
            foreach (var bars in beatBlend.Bars)
            {
                if (bars > longestBeat || bars < 2 || bars > 4 && !IsPowerOfTwo(bars)) continue;
                var overlap = bars * bar;
                var exit = ChooseExit(a, scene.SoundEndA, overlap, double.MinValue);
                if (exit < 0 || scene.VocalEndA + beat > exit + overlap)
                {
                    var covering = ChooseCoveringExit(a, overlap, scene.VocalEndA + beat, a.Duration + bar);
                    if (covering >= 0) exit = covering;
                    else if (scene.VocalEndA > exit + overlap + tolerance) exit = -1;
                }

                if (exit < 0) continue;
                var handoff = scene.VocalEndA > exit ? Math.Clamp((scene.VocalEndA - exit) / overlap, 0.5, 0.9) : 0.5;
                Blend(scene, beatBlend, pairing, bars, 0, exit, handoff, "phrase", null);
            }

            foreach (var bars in beatBlend.Bars)
            {
                if (bars > longestBeat || bars > 4 && !IsPowerOfTwo(bars)) continue;
                var exit = ChooseExit(a, scene.SoundEndA, bars * bar, scene.VocalEndA - tolerance);
                if (exit >= 0) Blend(scene, beatBlend, pairing, bars, 0, exit, 0.5, "after vocals", null);
            }
        }

        foreach (var bars in filterBlend.Bars)
        {
            if (bars > longestFilter) continue;
            var exit = ChooseExit(a, scene.SoundEndA, bars * bar, scene.VocalEndA - tolerance);
            if (exit >= 0) Blend(scene, filterBlend, pairing, bars, 0, exit, 0.5, "after vocals", null);
        }

        if (longestFilter >= 4)
        {
            var from = Math.Max(scene.VocalEndA - tolerance, scene.SoundEndA - longestFilter * bar);
            var exit = a.Beats.FirstOrDefault(t => t >= from, -1);
            var beats = exit < 0 ? 0 : (int)Math.Floor((scene.SoundEndA - exit) / beat);
            if (beats >= 2) Blend(scene, MixScoring.Template(MixShape.FilterBeats), pairing, 0, beats, exit, 0.5, "after vocals", null);
        }

        if (a.Sections is { Length: > 1 } sections)
        {
            var earliest = Math.Max(Math.Max(0, a.Duration - ExitWindowSeconds), scene.SoundEndA - options.MaxOverlapSeconds);
            for (var index = 1; index < sections.Length; index++)
            {
                if (sections[index].Start < earliest) continue;
                var exit = MixStructure.NearestDownbeat(a, sections[index].Start, 0.5 * bar);
                if (exit < 0) continue;
                var note = FormattableString.Invariant($"exit at section {exit:0.0}s");
                foreach (var template in new[] { beatBlend, filterBlend })
                {
                    if (template.Synced && !synced) continue;
                    foreach (var bars in template.Bars)
                    {
                        if (bars < 4 || bars > (template.Synced ? longestBeat : longestFilter) || !IsPowerOfTwo(bars)) continue;
                        var overlap = bars * bar;
                        if (exit + overlap > scene.SoundEndA + tolerance) continue;
                        var handoff = 0.5;
                        if (template.Synced && scene.VocalEndA > exit)
                        {
                            if (scene.VocalEndA + beat > exit + overlap)
                            {
                                scene.Reject(template, "section", bars, exit, overlap, firstDownbeatB, StillSinging);
                                continue;
                            }

                            handoff = Math.Clamp((scene.VocalEndA - exit) / overlap, 0.5, 0.9);
                        }

                        Blend(scene, template, pairing, bars, 0, exit, handoff, "section", note);
                    }
                }
            }
        }

        if (scene.Accepted.Count > 0) return "instead of a beat-aligned mix";
        if (scene.Rejected.Count > 0 && scene.Rejected.All(candidate => candidate.Rejected!.StartsWith(NoStableTempo, StringComparison.Ordinal)))
            return "no stable tempo at the mix points";
        return scene.Rejected.Count > 0 && scene.Rejected.All(candidate => candidate.Rejected == ExitPassed)
            ? "beat-aligned exits already passed"
            : "vocals run to the end";
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static void Blend(Scene scene, MixTemplate template, Pairing pairing, int bars, int beats, double exit, double handoff,
        string anchor, string? note)
    {
        var a = scene.A;
        var b = scene.B;
        var options = scene.Options;
        var whole = beats == 0;
        var nominal = whole ? bars * pairing.Bar : beats * pairing.Beat;
        var firstDownbeatB = b.Downbeats.FirstOrDefault(d => d >= scene.SoundStartB - 0.05);

        if (exit < options.EarliestExit)
        {
            scene.Reject(template, anchor, bars, exit, nominal, firstDownbeatB, ExitPassed);
            return;
        }

        if (MixStructure.Stable(a, exit, exit + nominal, false) is not { } tempoA)
        {
            scene.Reject(template, anchor, bars, exit, nominal, firstDownbeatB, NoStableTempo + " at A's exit");
            return;
        }

        var bpmA = MixStructure.LocalBpm(a, tempoA, exit, exit + nominal);
        var beat = 60 / bpmA;
        var bar = 4 * beat;
        var overlap = whole ? bars * bar : beats * beat;
        var (nominalRate, _) = BestRate(bpmA, pairing.EntryBpm);
        if (!template.Synced || !options.AllowTimeStretch) nominalRate = 1;

        var first = ChooseEntry(b, scene.VocalsB, firstDownbeatB, overlap * nominalRate, pairing.BarB);
        var entries = new List<(double Entry, string? Note, string Anchor)> { (first, null, anchor) };
        if (MixStructure.Stable(b, first, first + overlap * nominalRate, true) is null)
            foreach (var later in LaterEntries(scene, first, overlap * nominalRate, pairing.BarB))
                entries.Add((later.Entry, later.Note, anchor + ", " + later.Anchor));

        foreach (var (entryB, entryNote, entryAnchor) in entries)
        {
            if (MixStructure.Stable(b, entryB, entryB + overlap * nominalRate, true) is not { } tempoB)
            {
                scene.Reject(template, entryAnchor, bars, exit, overlap, entryB, NoStableTempo + " at B's entry");
                continue;
            }

            var bpmB = MixStructure.LocalBpm(b, tempoB, entryB, entryB + overlap * nominalRate);
            var (rate, multiplier) = BestRate(bpmA, bpmB);
            if (template.Synced && Math.Abs(rate - 1) > pairing.MaxDeviation)
            {
                scene.Reject(template, entryAnchor, bars, exit, overlap, entryB, "tempo difference beyond the stretch limit");
                continue;
            }

            if (!template.Synced || !options.AllowTimeStretch) rate = 1;
            if (!template.Synced && exit < scene.VocalEndA - pairing.Tolerance - 1e-9
                || scene.VocalEndA > exit + overlap + pairing.Tolerance + 1e-9)
            {
                scene.Reject(template, entryAnchor, bars, exit, overlap, entryB, StillSinging);
                continue;
            }

            var glide = options.AllowGlide && template.Synced && options.AllowTimeStretch ? MixGlide.Plan(rate, overlap, pairing.MaxDeviation) : null;
            var bOverlap = overlap * rate;
            var audible = overlap * (handoff + 0.7 * (1 - handoff));
            var clash = VocalClash(scene.VocalsA, exit, exit + audible, scene.VocalsB, entryB, entryB + bOverlap, rate);
            if (clash > MaximumVocalClash)
            {
                scene.Reject(template, entryAnchor, bars, exit, overlap, entryB, "vocal clash");
                continue;
            }

            var busy = VocalClash(scene.LeadsA, exit, exit + audible, scene.VocalsB, entryB, entryB + bOverlap, rate)
                       + VocalClash(scene.VocalsA, exit, exit + audible, scene.LeadsB, entryB, entryB + bOverlap, rate)
                       + VocalClash(scene.LeadsA, exit, exit + audible, scene.LeadsB, entryB, entryB + bOverlap, rate);
            var crowded = whole && bars >= 4 && clash + LeadClashWeight * busy > 1;

            var head = new List<string>
            {
                $"bpm {bpmA:0.0}->{bpmB:0.0} x{multiplier:0.##}",
                $"rate {rate:0.000}"
            };
            if (glide is not null) head.Add($"glide to {bpmB * multiplier:0.0} bpm");
            var keyDistance = KeyEstimator.CamelotDistance(a.Key, b.Key);
            if (keyDistance > 2 && a.KeyConfidence > MixScoring.TrustedKeyConfidence && b.KeyConfidence > MixScoring.TrustedKeyConfidence)
                head.Add($"key distance {keyDistance}");
            if (scene.VocalEndA > 0) head.Add($"vocals end {scene.VocalEndA:0.0}s");
            var tail = new List<string>();
            if (note is not null) tail.Add(note);
            if (entryNote is not null) tail.Add(entryNote);
            else if (entryB > firstDownbeatB + 0.05) tail.Add($"skip intro to {entryB:0.0}s");
            tail.Add(whole ? $"{bars} bars" : $"{beats} beats");

            var plan = new MixPlan
            {
                Technique = template.Technique,
                AExit = Refine(exit, tempoA),
                Overlap = overlap,
                BEntry = Refine(entryB, tempoB),
                Rate = rate,
                RampBack = glide is null && Math.Abs(rate - 1) > 0.0005 ? Math.Min(16 * 4 * 60 / (bpmB * multiplier), 20) : 0,
                Handoff = handoff,
                Bars = (int)Math.Round(overlap / bar),
                TempoA = bpmA,
                TempoB = bpmB,
                Glide = glide
            };
            var cut = whole && template.Technique == MixTechnique.FilterCut && options.TailEffects
                ? EchoCut(a, exit + overlap, scene.VocalEndA, beat)
                : -1;
            if (cut > exit)
            {
                tail.Add("echo out");
                plan = plan with
                {
                    Overlap = cut - plan.AExit,
                    RampBack = 0,
                    Tail = MixTail.Echo,
                    TailDelay = beat,
                    TailOpen = cut - beat,
                    TailFull = cut - 0.75 * beat,
                    TailLength = Math.Clamp(6 * beat + 0.5, 2, 5)
                };
            }

            plan = MatchLoudness(plan with { Reason = string.Join(", ", head.Concat(tail)) }, a, b, options);
            var seconds = plan.Overlap;
            var exitDb = MixLevels.Relative(a, exit - 2, Math.Min(scene.SoundEndA, exit + Math.Max(2, 0.5 * seconds)), out var exitSeconds);
            var entryDb = MixLevels.Relative(b, entryB + 0.5 * bOverlap, entryB + bOverlap + 4, out var entrySeconds);
            var score = new MixScore(
                template.Prior,
                MixScoring.Length(template.Shape, whole ? bars : 0),
                MixScoring.Tempo(template.Shape, rate, multiplier, bpmA, bpmB, seconds),
                MixScoring.Key(template.Technique, a.Key, a.KeyConfidence, b.Key, b.KeyConfidence, seconds),
                MixScoring.Vocals(clash),
                MixScoring.Crowd(crowded),
                MixScoring.Hold(handoff),
                MixScoring.Energy(exitDb, exitSeconds, entryDb, entrySeconds),
                MixScoring.Exit(a, exit, beat, scene.VocalsA) + MixScoring.Phrase(OnPhrase(a, exit)),
                MixScoring.Entry(b, entryB, pairing.BarB),
                MixScoring.Cut(scene.SoundEndA - plan.AFadeEnd, entryB - firstDownbeatB),
                MixScoring.Loudness(plan.Loudness),
                MixScoring.Repeat(template.Technique, scene.Previous));
            scene.Add(new MixCandidate(template, entryAnchor, plan, score, crowded) { Head = head, Tail = tail, Clash = clash });
        }
    }

    private static double Refine(double time, TempoFit fit)
    {
        var snapped = fit.Snap(time);
        var shift = Math.Abs(snapped - time);
        return shift < RefineFloor || shift > 2 * TempoRuns.ToleranceSeconds ? time : snapped;
    }

    private static IEnumerable<(double Entry, string Note, string Anchor)> LaterEntries(Scene scene, double first, double bOverlap, double barB)
    {
        var b = scene.B;
        var latest = b.Duration * 0.4;
        var found = -1.0;
        if (b.TempoRuns is { Length: > 0 } runs)
        {
            foreach (var run in runs)
            {
                if (run.End - run.Start < bOverlap || run.Start <= first) continue;
                var entry = b.Downbeats.FirstOrDefault(d => d >= run.Start - 0.05, -1);
                if (entry < 0 || entry > latest) break;
                found = entry;
                yield return (entry, FormattableString.Invariant($"enter at stable tempo {entry:0.0}s"), "stable run");
                break;
            }
        }

        if (b.Sections is not { Length: > 1 } sections) yield break;
        for (var index = 1; index < sections.Length; index++)
        {
            if (sections[index].Start <= first || sections[index].Start > latest) continue;
            var entry = MixStructure.NearestDownbeat(b, sections[index].Start, 0.5 * barB);
            if (entry < 0 || entry == found) continue;
            if (MixStructure.Stable(b, entry, entry + bOverlap, true) is null) continue;
            yield return (entry, FormattableString.Invariant($"enter at section {entry:0.0}s"), "section entry");
            yield break;
        }
    }

    private static MixCandidate Fade(Scene scene, string reason)
    {
        var a = scene.A;
        var b = scene.B;
        var plan = Crossfade(a, scene.VocalsB, scene.SoundEndA, scene.SoundStartB, scene.VocalEndA, scene.Options, reason);
        plan = MatchLoudness(plan, a, b, scene.Options);
        var shape = plan.Tail == MixTail.Reverb ? MixShape.ReverbTail : plan.EntryFade > 0 ? MixShape.PlayOut : MixShape.Crossfade;
        var template = MixScoring.Template(shape);
        var measured = a.HasBeatGrid && b.HasBeatGrid && a.BeatConfidence >= TrustedBeatConfidence && b.BeatConfidence >= TrustedBeatConfidence;
        var shared = shape == MixShape.Crossfade ? plan.Overlap : Math.Min(plan.Overlap, Math.Max(plan.EntryFade, MinimumEntryFade));
        var clash = VocalClash(scene.VocalsA, plan.AExit, plan.AFadeEnd, scene.VocalsB, plan.BEntry, plan.BEntry + plan.Overlap, 1);
        var exitDb = MixLevels.Relative(a, plan.AExit - 2, Math.Min(scene.SoundEndA, plan.AExit + Math.Max(2, 0.5 * plan.Overlap)), out var exitSeconds);
        var entryDb = MixLevels.Relative(b, plan.BEntry + 0.5 * plan.Overlap, plan.BEntry + plan.Overlap + 4, out var entrySeconds);
        var score = new MixScore(
            template.Prior,
            0,
            MixScoring.Tempo(shape, 1, 1, measured ? a.Bpm : 0, measured ? b.Bpm : 0, shared),
            MixScoring.Key(MixTechnique.Crossfade, a.Key, a.KeyConfidence, b.Key, b.KeyConfidence, shared),
            MixScoring.Vocals(clash),
            0,
            0,
            shape == MixShape.Crossfade ? MixScoring.Energy(exitDb, exitSeconds, entryDb, entrySeconds) : 0,
            0,
            0,
            0,
            MixScoring.Loudness(plan.Loudness),
            MixScoring.Repeat(MixTechnique.Crossfade, scene.Previous));
        return new MixCandidate(template, "ending", plan, score) { Clash = clash };
    }

    private static MixPlan MatchLoudness(MixPlan plan, TrackAnalysis a, TrackAnalysis b, AutoMixPlannerOptions options)
    {
        var soundEndA = a.SoundEnd > 0 ? a.SoundEnd : a.Duration;
        var soundEndB = b.SoundEnd > 0 ? b.SoundEnd : b.Duration;
        var exitTo = Math.Min(plan.AExit + 0.5 * plan.Overlap, soundEndA);
        if (plan.EntryFade > 0 && plan.Ending is { } ending) exitTo = Math.Min(exitTo, ending.DecayStart);
        var exitFrom = Math.Max(Math.Max(0, a.SoundStart), exitTo - MixLevels.ExitWindowSeconds);
        var entryLength = Math.Max(MixLevels.EntryWindowSeconds, plan.Overlap * plan.Rate + MixLevels.EntryTailSeconds);
        var entryTo = Math.Min(plan.BEntry + entryLength, soundEndB);
        var loudness = MixLevels.Match(a, b, exitFrom, exitTo, plan.BEntry, entryTo, options.PlaybackOffsetDb);
        return plan with
        {
            GainB = Math.Pow(10, loudness.GainDb / 20),
            GainRelease = loudness.Release,
            Loudness = loudness
        };
    }

    private static MixPlan Crossfade(TrackAnalysis a, IReadOnlyList<(double Start, double End)> vocalsB, double soundEndA, double soundStartB,
        double vocalEndA, AutoMixPlannerOptions options, string reason)
    {
        var ending = MixEnding.Detect(a);
        if (ending.IsNatural) return PlayOut(ending, vocalsB, soundEndA, soundStartB, vocalEndA, options, reason);
        if (ending.Kind == MixEndingKind.Hard) reason += ", " + ending.Describe();
        if (ending.Kind == MixEndingKind.Hard && options.TailEffects)
            return ReverbTail(a, ending, vocalsB, soundEndA, soundStartB, vocalEndA, reason);
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
            Ending = ending.Kind == MixEndingKind.Unknown ? null : ending,
            Reason = reason
        };
    }

    private static MixPlan ReverbTail(TrackAnalysis a, MixEnding ending, IReadOnlyList<(double Start, double End)> vocalsB,
        double soundEndA, double soundStartB, double vocalEndA, string reason)
    {
        var bodyEnd = Math.Min(ending.BodyEnd, soundEndA);
        var lead = Math.Clamp(FirstVocalStart(vocalsB, soundStartB) - soundStartB, 0, 3);
        var entry = Math.Clamp(bodyEnd - 0.5 - lead / 6, 0, Math.Max(0, soundEndA - MinimumPlayOutOverlap));
        var delay = 0.0;
        if (a.HasBeatGrid && a.BeatConfidence >= TrustedBeatConfidence)
        {
            delay = 30 / a.Bpm;
            while (delay > 0.4) delay /= 2;
            while (delay < 0.14) delay *= 2;
        }

        if (vocalEndA > entry) reason += $", vocals end {vocalEndA:0.0}s";
        reason += ", reverb tail";
        return new MixPlan
        {
            Technique = MixTechnique.Crossfade,
            AExit = entry,
            Overlap = soundEndA - entry,
            BEntry = soundStartB,
            Rate = 1,
            Handoff = 1,
            Ending = ending,
            EntryFade = ReverbEntryFade,
            Tail = MixTail.Reverb,
            TailDelay = delay,
            TailOpen = bodyEnd - ReverbOpenSeconds,
            TailFull = bodyEnd - ReverbFullSeconds,
            TailBloom = bodyEnd,
            TailLength = ReverbTailLength,
            Reason = reason
        };
    }

    internal static double EchoCut(TrackAnalysis a, double cut, double vocalEndA, double beat)
    {
        if (a.DownbeatConfidence < TrustedBarConfidence || beat < 0.3 || beat > MixSend.MaxDelaySeconds) return -1;
        var line = -1.0;
        foreach (var downbeat in a.Downbeats)
            if (Math.Abs(downbeat - cut) <= BarLineTolerance && (line < 0 || Math.Abs(downbeat - cut) < Math.Abs(line - cut))) line = downbeat;
        if (line < 0 || vocalEndA > line - beat) return -1;
        return line;
    }

    private static MixPlan PlayOut(MixEnding ending, IReadOnlyList<(double Start, double End)> vocalsB, double soundEndA, double soundStartB,
        double vocalEndA, AutoMixPlannerOptions options, string reason)
    {
        var entry = ending.EntryPoint;
        reason += ", " + ending.Describe();
        if (vocalEndA > entry)
        {
            var lead = Math.Clamp(FirstVocalStart(vocalsB, soundStartB) - soundStartB, 0, 3);
            entry = Math.Max(entry, Math.Min(vocalEndA, ending.VocalFloor) - lead);
            reason += $", vocals end {vocalEndA:0.0}s";
        }

        entry = Math.Max(entry, soundEndA - Math.Max(MinimumPlayOutOverlap, options.MaxOverlapSeconds));
        entry = Math.Clamp(entry, 0, Math.Max(0, soundEndA - MinimumPlayOutOverlap));
        var fade = Math.Clamp(ending.DeepPoint - ending.EntryPoint, MinimumEntryFade, MaximumEntryFade);
        reason += FormattableString.Invariant($", A plays out, B eases in over {fade:0.0}s");
        return new MixPlan
        {
            Technique = MixTechnique.Crossfade,
            AExit = entry,
            Overlap = soundEndA - entry,
            BEntry = soundStartB,
            Rate = 1,
            Handoff = 1,
            Ending = ending,
            EntryFade = fade,
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

    private static bool OnPhrase(TrackAnalysis a, double exit)
    {
        var downbeats = a.Downbeats;
        var index = Array.FindIndex(downbeats, d => Math.Abs(d - exit) < 0.02);
        if (index < 0) return false;
        var anchor = a.OutroStart > 0 ? Array.FindIndex(downbeats, d => d >= a.OutroStart - 0.05) : -1;
        return ((index - Math.Max(0, anchor)) % 4 + 4) % 4 == 0;
    }

    private static double ChooseExit(TrackAnalysis a, double soundEndA, double overlap, double earliest)
    {
        var downbeats = a.Downbeats;
        var anchor = a.OutroStart > 0 ? Array.FindIndex(downbeats, d => d >= a.OutroStart - 0.05) : -1;
        var limit = soundEndA - overlap;
        var minimum = Math.Max(Math.Max(0, a.Duration - ExitWindowSeconds), earliest);
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
        var minimum = Math.Max(0, a.Duration - ExitWindowSeconds);
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
}
