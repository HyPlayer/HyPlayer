using System;
using HyPlayer.Features.Playback.Analysis;

namespace HyPlayer.Features.Playback.AutoMix;

public enum MixEndingKind
{
    Unknown,
    Hard,
    RingOut,
    FadeOut
}

public readonly record struct MixEnding(
    MixEndingKind Kind,
    double Top,
    double DecayStart,
    double BodyEnd,
    double EntryPoint,
    double VocalFloor,
    double DeepPoint,
    double Length,
    double Slope)
{
    public const double TopMarginDb = 3;
    public const double SpanDb = 20;
    public const double MinimumSpanDb = 8;
    public const double FloorDb = -50;
    public const double MaxSlope = 20;
    public const double MinimumOnsetSeconds = 0.5;
    public const double MinimumLengthSeconds = 1.5;
    public const double MinimumDepthDb = 15;
    public const double FadeOutSeconds = 5;
    public const double FadeOutSlope = 8;
    public const double BodyDb = 10;
    public const double EntryDb = 8;
    public const double VocalDb = 14;
    public const double DeepDb = 20;
    public const double SteadyShare = 0.65;
    public const double CutSlope = 1000;
    private static readonly double[] Windows = [12, 24, 48];

    public bool IsNatural => Kind is MixEndingKind.RingOut or MixEndingKind.FadeOut;

    public string Describe() => Kind switch
    {
        MixEndingKind.FadeOut => FormattableString.Invariant($"fade-out {Length:0.0}s at {Slope:0.0} dB/s"),
        MixEndingKind.RingOut => FormattableString.Invariant($"ring-out {Length:0.0}s at {Slope:0.0} dB/s"),
        MixEndingKind.Hard => "hard ending",
        _ => "ending unknown"
    };

    public static MixEnding Detect(TrackAnalysis track)
    {
        var envelope = track.Envelope;
        var rate = MixLevels.FramesPerSecond(track);
        var soundEnd = track.SoundEnd > 0 ? track.SoundEnd : track.Duration;
        var end = Math.Min(envelope.Length, (int)Math.Round(soundEnd * rate));
        var start = Math.Clamp((int)Math.Ceiling(Math.Max(0, track.SoundStart) * rate), 0, end);
        if (end - start < (int)(5 * rate)) return default;

        var endLevel = Math.Min(envelope[end - 1], envelope[end - 2]);

        MixEnding result = default;
        foreach (var window in Windows)
        {
            var from = Math.Max(start, end - (int)Math.Round(window * rate));
            var candidate = Measure(envelope, rate, from, end, soundEnd, endLevel, result.Kind != MixEndingKind.Unknown);
            if (result.Kind == MixEndingKind.Unknown)
            {
                result = candidate;
                if (!result.IsNatural) break;
            }
            else if (candidate.IsNatural && candidate.Top >= result.Top + TopMarginDb)
            {
                result = candidate;
            }
            else
            {
                break;
            }

            if (from == start) break;
        }

        return result;
    }

    private static MixEnding Measure(double[] envelope, double rate, int from, int end, double soundEnd, double endLevel, bool steady)
    {
        var top = double.MinValue;
        for (var i = from; i < end; i++) top = Math.Max(top, envelope[i]);
        double Last(double level)
        {
            for (var i = end - 1; i >= from; i--)
                if (envelope[i] >= level) return Math.Min(soundEnd, (i + 1) / rate);
            return from / rate;
        }

        if (endLevel > top - MinimumDepthDb && endLevel > FloorDb + 2)
        {
            var stop = Last(top - BodyDb);
            return new MixEnding(MixEndingKind.Hard, top, stop, stop, stop, stop, stop, soundEnd - stop, CutSlope);
        }

        var high = top - TopMarginDb;
        var low = Math.Max(high - SpanDb, Math.Max(FloorDb, endLevel) + 2);
        var span = high - low;
        if (span < MinimumSpanDb) return default;

        var decayStart = Last(high);
        var decayEnd = Last(low);
        var length = soundEnd - decayStart;
        var seconds = Math.Max(1 / rate, decayEnd - decayStart);
        var slope = span / seconds;
        var onsetDb = Math.Min(10, span / 2);
        var onset = Last(high - onsetDb) - decayStart;
        var body = Last(top - BodyDb);
        var entry = Last(top - EntryDb);
        var vocal = Last(Math.Max(top - VocalDb, low));
        var deep = Last(Math.Max(top - DeepDb, low));

        var natural = slope <= MaxSlope
                      && onset >= MinimumOnsetSeconds * onsetDb / 10
                      && length >= MinimumLengthSeconds;
        if (natural && steady)
        {
            var previous = decayStart;
            for (var band = 1; band <= 4 && natural; band++)
            {
                var reached = Last(high - span * band / 4);
                if (reached - previous > SteadyShare * seconds) natural = false;
                previous = reached;
            }
        }

        var kind = !natural ? MixEndingKind.Hard
            : length >= FadeOutSeconds && slope <= FadeOutSlope ? MixEndingKind.FadeOut
            : MixEndingKind.RingOut;
        return new MixEnding(kind, top, decayStart, body, entry, vocal, deep, length, slope);
    }
}
