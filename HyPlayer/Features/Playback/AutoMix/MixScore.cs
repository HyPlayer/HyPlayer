using System;
using System.Collections.Generic;
using HyPlayer.Features.Playback.Analysis;
using HyPlayer.Features.Playback.Analysis.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public enum MixShape
{
    Gapless,
    BeatBlend,
    FilterBlend,
    FilterBeats,
    PlayOut,
    ReverbTail,
    Crossfade
}

public sealed record MixTemplate(string Name, MixShape Shape, MixTechnique Technique, double Prior, int[] Bars, bool Synced);

public readonly record struct MixScore(
    double Template,
    double Length,
    double Tempo,
    double Key,
    double Vocals,
    double Crowd,
    double Hold,
    double Energy,
    double Exit,
    double Entry,
    double Cut,
    double Loudness,
    double Repeat)
{
    public double Total => Template + Length + Tempo + Key + Vocals + Crowd + Hold + Energy + Exit + Entry + Cut + Loudness + Repeat;

    public string Describe() => FormattableString.Invariant(
        $"{Total:+0.00;-0.00;0.00} = template {Template:+0.0;-0.0;0} length {Length:+0.0;-0.0;0} tempo {Tempo:+0.00;-0.00;0} key {Key:+0.00;-0.00;0} vocals {Vocals:+0.00;-0.00;0} crowd {Crowd:+0.0;-0.0;0} hold {Hold:+0.00;-0.00;0} energy {Energy:+0.00;-0.00;0} exit {Exit:+0.00;-0.00;0} entry {Entry:+0.00;-0.00;0} cut {Cut:+0.00;-0.00;0} loudness {Loudness:+0.00;-0.00;0} repeat {Repeat:+0.0;-0.0;0}");
}

public static class MixScoring
{
    public const double VarietyMargin = 1;
    public const double RepeatPenalty = 3;
    public const double CrowdPenalty = 3;
    public const double ShortBlend = 6;
    public const double ShortBlendPerBar = 0.5;
    public const double BeatsBlend = 3;
    public const double LengthPerDoubling = 0.5;
    public const double StretchPerPercent = 0.5;
    public const double OctavePenalty = 0.5;
    public const double UnsyncedFullMismatch = 0.08;
    public const double UnsyncedFullSeconds = 16;
    public const double FilteredUnsynced = 0.5;
    public const double KeyPerStepSecond = 0.03;
    public const double KeyLimit = 4;
    public const double TrustedKeyConfidence = 0.03;
    public const double ClashPerSecond = 1.5;
    public const double HoldPenalty = 2;
    public const double EnergyFreeDb = 3;
    public const double EnergyPerDb = 0.1;
    public const double EnergyLimit = 1.5;
    public const double BoundaryBase = 1;
    public const double OutroBonus = 0.75;
    public const double QuietBonus = 0.75;
    public const double VocalFreeBonus = 0.5;
    public const double EntryBonus = 1;
    public const double PhraseBonus = 0.3;
    public const double EndingCutPerSecond = 0.12;
    public const double EndingCutLimit = 5;
    public const double IntroCutPerSecond = 0.05;
    public const double IntroCutLimit = 2;
    public const double GainPerDb = 0.15;
    public const double GainLimit = 1;
    public const double BoostLimitedPenalty = 0.5;

    public static readonly IReadOnlyList<MixTemplate> Templates =
    [
        new("gapless", MixShape.Gapless, MixTechnique.Gapless, 0, [], false),
        new("beat mix", MixShape.BeatBlend, MixTechnique.BeatMix, 0, [32, 16, 8, 4, 3, 2, 1], true),
        new("filter cut", MixShape.FilterBlend, MixTechnique.FilterCut, -3, [8, 4], false),
        new("filter cut by beats", MixShape.FilterBeats, MixTechnique.FilterCut, -3, [], false),
        new("play out", MixShape.PlayOut, MixTechnique.Crossfade, -5, [], false),
        new("reverb tail", MixShape.ReverbTail, MixTechnique.Crossfade, -8, [], false),
        new("crossfade", MixShape.Crossfade, MixTechnique.Crossfade, -9, [], false)
    ];

    public static MixTemplate Template(MixShape shape)
    {
        foreach (var template in Templates)
            if (template.Shape == shape) return template;
        throw new ArgumentOutOfRangeException(nameof(shape));
    }

    public static double Length(MixShape shape, int bars)
    {
        return shape switch
        {
            MixShape.FilterBeats => -BeatsBlend,
            MixShape.BeatBlend or MixShape.FilterBlend => bars < 4
                ? -ShortBlend - ShortBlendPerBar * (4 - bars)
                : LengthPerDoubling * Math.Log2(bars / 4.0),
            _ => 0
        };
    }

    public static double Tempo(MixShape shape, double rate, double multiplier, double bpmA, double bpmB, double overlapSeconds)
    {
        if (shape == MixShape.BeatBlend)
            return -StretchPerPercent * 100 * Math.Abs(rate - 1) - (Math.Abs(multiplier - 1) > 1e-9 ? OctavePenalty : 0);
        if (shape == MixShape.Gapless || bpmA <= 0 || bpmB <= 0) return 0;
        var weight = shape is MixShape.FilterBlend or MixShape.FilterBeats ? FilteredUnsynced : 1;
        return -weight * Math.Min(1, Mismatch(bpmA, bpmB) / UnsyncedFullMismatch) * Math.Min(1, overlapSeconds / UnsyncedFullSeconds);
    }

    public static double Mismatch(double bpmA, double bpmB)
    {
        var best = double.MaxValue;
        foreach (var multiplier in new[] { 1.0, 2.0, 0.5 })
            best = Math.Min(best, Math.Abs(bpmA / (bpmB * multiplier) - 1));
        return best;
    }

    public static double Key(MixTechnique technique, int keyA, double confidenceA, int keyB, double confidenceB, double overlapSeconds)
    {
        if (confidenceA <= TrustedKeyConfidence || confidenceB <= TrustedKeyConfidence) return 0;
        var distance = KeyEstimator.CamelotDistance(keyA, keyB);
        if (distance <= 1) return 0;
        var weight = technique switch
        {
            MixTechnique.BeatMix => 1,
            MixTechnique.FilterCut => 0.6,
            _ => 0.8
        };
        return -Math.Min(KeyLimit, KeyPerStepSecond * (distance - 1) * overlapSeconds * weight);
    }

    public static double Vocals(double clashSeconds)
    {
        return -ClashPerSecond * Math.Max(0, clashSeconds);
    }

    public static double Crowd(bool crowded)
    {
        return crowded ? -CrowdPenalty : 0;
    }

    public static double Hold(double handoff)
    {
        return handoff > 0.5 ? -HoldPenalty * Math.Min(1, (handoff - 0.5) / 0.4) : 0;
    }

    public static double Energy(double exitDb, double exitSeconds, double entryDb, double entrySeconds)
    {
        if (exitSeconds <= 0 || entrySeconds <= 0) return 0;
        return -Math.Min(EnergyLimit, EnergyPerDb * Math.Max(0, Math.Abs(exitDb - entryDb) - EnergyFreeDb));
    }

    public static double Exit(TrackAnalysis a, double exit, double tolerance, IReadOnlyList<(double Start, double End)> vocals)
    {
        var index = MixStructure.SectionAt(a, exit, tolerance);
        if (index <= 0) return 0;
        var sections = a.Sections!;
        var section = sections[index];
        var score = BoundaryBase * (0.5 + 0.5 * Math.Clamp(section.Strength, 0, 1));
        if (index == sections.Length - 1) score += OutroBonus;
        if (MixStructure.IsQuieter(sections, index)) score += QuietBonus;
        if (MixStructure.VocalShare(section.Start, section.End, vocals) < MixStructure.VocalFreeShare) score += VocalFreeBonus;
        return score;
    }

    public static double Phrase(bool onPhrase)
    {
        return onPhrase ? PhraseBonus : 0;
    }

    public static double Entry(TrackAnalysis b, double entry, double tolerance)
    {
        return MixStructure.SectionAt(b, entry, tolerance) >= 0 ? EntryBonus : 0;
    }

    public static double Cut(double endingSeconds, double introSeconds)
    {
        return -Math.Min(EndingCutLimit, EndingCutPerSecond * Math.Max(0, endingSeconds))
               - Math.Min(IntroCutLimit, IntroCutPerSecond * Math.Max(0, introSeconds));
    }

    public static double Loudness(MixLoudness? loudness)
    {
        if (loudness is not { } value) return 0;
        return -Math.Min(GainLimit, GainPerDb * Math.Abs(value.GainDb)) - (value.BoostLimited ? BoostLimitedPenalty : 0);
    }

    public static double Repeat(MixTechnique technique, MixTechnique? previous)
    {
        return previous == technique ? -RepeatPenalty : 0;
    }
}
