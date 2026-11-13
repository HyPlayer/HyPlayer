using System;
using System.Collections;
using System.Collections.Generic;

namespace HyPlayer.Features.Playback.AutoMix;

public sealed class MixGlide : IReadOnlyList<MixRatePoint>, IEquatable<MixGlide>
{
    public const int Segments = 64;
    public const double MinimumDifference = 0.003;
    public const double MaximumChangePerSecond = 0.01;

    private readonly MixRatePoint[] _points;

    private MixGlide(MixRatePoint[] points) => _points = points;

    public int Count => _points.Length;
    public MixRatePoint this[int index] => _points[index];

    public static MixGlide Between(double rate)
    {
        var points = new MixRatePoint[Segments + 1];
        var span = -Math.Log(rate);
        for (var i = 1; i < Segments; i++)
        {
            var position = i / (double)Segments;
            var eased = position * position * (3 - 2 * position);
            var rateA = Math.Exp(span * eased);
            points[i] = new MixRatePoint(position, rateA, rateA * rate);
        }

        points[0] = new MixRatePoint(0, 1, rate);
        points[Segments] = new MixRatePoint(1, 1 / rate, 1);
        return new MixGlide(points);
    }

    public static MixGlide? Plan(double rate, double overlapSeconds, double maxDeviation)
    {
        if (!(rate > 0) || !(overlapSeconds > 0) || Math.Abs(rate - 1) < MinimumDifference) return null;
        if (Math.Abs(rate - 1) > maxDeviation || Math.Abs(1 / rate - 1) > maxDeviation) return null;
        var glide = Between(rate);
        return ChangePerSecond(glide, overlapSeconds) <= MaximumChangePerSecond ? glide : null;
    }

    public static double ChangePerSecond(IReadOnlyList<MixRatePoint> points, double overlapSeconds)
    {
        var steepest = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var span = points[i].Position - points[i - 1].Position;
            if (span > 0) steepest = Math.Max(steepest, Math.Abs(points[i].RateA - points[i - 1].RateA) / span);
        }

        return steepest / overlapSeconds;
    }

    public static double Seconds(IReadOnlyList<MixRatePoint> points, double overlapSeconds)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var span = points[i].Position - points[i - 1].Position;
            var from = points[i - 1].RateA;
            var to = points[i].RateA;
            total += Math.Abs(to - from) < 1e-12 ? span / from : span * Math.Log(to / from) / (to - from);
        }

        return total * overlapSeconds;
    }

    public static double Deviation(IReadOnlyList<MixRatePoint> points)
    {
        var worst = 0.0;
        foreach (var point in points)
            worst = Math.Max(worst, Math.Max(Math.Abs(point.RateA - 1), Math.Abs(point.RateB - 1)));
        return worst;
    }

    public static string Describe(MixPlan plan)
    {
        if (plan.Glide is not { Count: > 0 } glide) return "none";
        var first = glide[0];
        var last = glide[glide.Count - 1];
        return FormattableString.Invariant(
            $"{plan.TempoA * first.RateA:0.0}->{plan.TempoA * last.RateA:0.0} bpm over {Seconds(glide, plan.Overlap):0.0}s, A x{first.RateA:0.0000}->x{last.RateA:0.0000}, B x{first.RateB:0.0000}->x{last.RateB:0.0000}, max rate deviation {100 * Deviation(glide):0.00}%, up to {100 * ChangePerSecond(glide, plan.Overlap):0.00}%/s");
    }

    public static void Rates(MixRatePoint[] points, double position, ref int cursor, out double rateA, out double rateB)
    {
        var last = points.Length - 1;
        while (cursor < last - 1 && position >= points[cursor + 1].Position) cursor++;
        var from = points[cursor];
        var to = points[Math.Min(last, cursor + 1)];
        var span = to.Position - from.Position;
        var weight = span <= 0 ? 1 : (position - from.Position) / span;
        if (weight >= 1)
        {
            rateA = to.RateA;
            rateB = to.RateB;
        }
        else if (weight <= 0)
        {
            rateA = from.RateA;
            rateB = from.RateB;
        }
        else
        {
            rateA = from.RateA + (to.RateA - from.RateA) * weight;
            rateB = from.RateB + (to.RateB - from.RateB) * weight;
        }
    }

    public bool Equals(MixGlide? other)
    {
        return other is not null && _points.AsSpan().SequenceEqual(other._points);
    }

    public override bool Equals(object? obj) => Equals(obj as MixGlide);

    public override int GetHashCode()
    {
        return HashCode.Combine(_points.Length, _points[0], _points[^1]);
    }

    public IEnumerator<MixRatePoint> GetEnumerator() => ((IEnumerable<MixRatePoint>)_points).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _points.GetEnumerator();
}
