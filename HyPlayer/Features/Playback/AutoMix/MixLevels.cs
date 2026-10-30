using System;
using HyPlayer.Features.Playback.Analysis;

namespace HyPlayer.Features.Playback.AutoMix;

public readonly record struct MixLoudness(
    double WholeDb,
    double ExitDb,
    double ExitSeconds,
    double EntryDb,
    double EntrySeconds,
    double Weight,
    double GainDb,
    double Release,
    bool BoostLimited = false)
{
    public string Describe() => FormattableString.Invariant(
        $"whole {WholeDb:+0.0;-0.0;0.0} dB, exit A {ExitDb:+0.0;-0.0;0.0} dB over {ExitSeconds:0.0}s, entry B {EntryDb:+0.0;-0.0;0.0} dB over {EntrySeconds:0.0}s, local weight {Weight:0.00} -> gainB {GainDb:+0.0;-0.0;0.0} dB{(BoostLimited ? " (boost limited by headroom)" : string.Empty)}, release {Release:0.0}s");
}

public static class MixLevels
{
    public const double ExitWindowSeconds = 10;
    public const double EntryWindowSeconds = 8;
    public const double EntryTailSeconds = 4;
    public const double MinimumWindowSeconds = 3;
    public const double FullWeightSeconds = 6;
    public const double LocalShare = 0.7;
    public const double MaxGainDb = 6;
    public const double BaseReleaseSeconds = 2;
    public const double ReleaseSecondsPerDb = 1.25;
    public const double MaxReleaseSeconds = 9.5;
    public const double BoostCeilingDb = -8;
    private const double AbsoluteGateDb = -70;
    private const double RelativeGateDb = 10;

    public static double FramesPerSecond(TrackAnalysis track)
    {
        var nominal = track.EnvelopeRate > 0 ? track.EnvelopeRate : 10;
        if (track.Duration <= 0 || track.Envelope.Length == 0) return nominal;
        var actual = track.Envelope.Length / track.Duration;
        return Math.Abs(actual - nominal) <= nominal * 0.03 ? actual : nominal;
    }

    public static double Level(TrackAnalysis track, double from, double to, out double seconds)
    {
        seconds = 0;
        var envelope = track.Envelope;
        if (envelope.Length == 0 || to <= from) return double.NaN;
        var rate = FramesPerSecond(track);
        var first = Math.Clamp((int)Math.Round(from * rate), 0, envelope.Length);
        var last = Math.Clamp((int)Math.Round(to * rate), 0, envelope.Length);
        if (last <= first) return double.NaN;

        double sum = 0;
        var count = 0;
        for (var i = first; i < last; i++)
        {
            if (envelope[i] <= AbsoluteGateDb) continue;
            sum += Math.Pow(10, envelope[i] / 10);
            count++;
        }

        if (count == 0) return double.NaN;
        var threshold = 10 * Math.Log10(sum / count) - RelativeGateDb;
        sum = 0;
        count = 0;
        for (var i = first; i < last; i++)
        {
            if (envelope[i] <= AbsoluteGateDb || envelope[i] <= threshold) continue;
            sum += Math.Pow(10, envelope[i] / 10);
            count++;
        }

        if (count == 0) return double.NaN;
        seconds = count / rate;
        return 10 * Math.Log10(sum / count);
    }

    public static double TrackLevel(TrackAnalysis track)
    {
        var end = track.SoundEnd > 0 ? track.SoundEnd : track.Duration;
        return Level(track, Math.Max(0, track.SoundStart), end, out _);
    }

    public static double Relative(TrackAnalysis track, double from, double to, out double seconds)
    {
        var level = Level(track, from, to, out seconds);
        var whole = TrackLevel(track);
        if (double.IsNaN(level) || double.IsNaN(whole))
        {
            seconds = 0;
            return 0;
        }

        return level - whole;
    }

    public static MixLoudness Match(TrackAnalysis a, TrackAnalysis b, double exitFrom, double exitTo,
        double entryFrom, double entryTo, double playbackOffsetDb)
    {
        if (a.IntegratedLoudness <= -60 || b.IntegratedLoudness <= -60)
            return new MixLoudness(0, 0, 0, 0, 0, 0, 0, BaseReleaseSeconds);

        var whole = a.IntegratedLoudness - b.IntegratedLoudness + playbackOffsetDb;
        var exit = Relative(a, exitFrom, exitTo, out var exitSeconds);
        var entry = Relative(b, entryFrom, entryTo, out var entrySeconds);
        var weight = LocalShare * Math.Min(Confidence(exitSeconds), Confidence(entrySeconds));
        var gain = Math.Clamp(whole + weight * (exit - entry), -MaxGainDb, MaxGainDb);
        var limited = false;
        if (gain > 0)
        {
            var loudest = Loudest(b, entryFrom, entryTo);
            var ceiling = double.IsNaN(loudest) ? gain : Math.Max(0, BoostCeilingDb - loudest);
            limited = ceiling < gain;
            gain = Math.Min(gain, ceiling);
        }

        var release = weight <= 0
            ? BaseReleaseSeconds
            : Math.Clamp(BaseReleaseSeconds + ReleaseSecondsPerDb * Math.Abs(gain), BaseReleaseSeconds, MaxReleaseSeconds);
        return new MixLoudness(whole, exit, exitSeconds, entry, entrySeconds, weight, gain, release, limited);
    }

    private static double Loudest(TrackAnalysis track, double from, double to)
    {
        var envelope = track.Envelope;
        if (envelope.Length == 0 || to <= from) return double.NaN;
        var rate = FramesPerSecond(track);
        var first = Math.Clamp((int)Math.Round(from * rate), 0, envelope.Length);
        var last = Math.Clamp((int)Math.Round(to * rate), 0, envelope.Length);
        var loudest = double.NaN;
        for (var i = first; i < last; i++)
            if (double.IsNaN(loudest) || envelope[i] > loudest) loudest = envelope[i];
        return loudest;
    }

    private static double Confidence(double seconds)
    {
        return Math.Clamp((seconds - MinimumWindowSeconds) / (FullWeightSeconds - MinimumWindowSeconds), 0, 1);
    }
}
