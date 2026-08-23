#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.LyricRenderer.Text;

internal readonly record struct FocusedLineRevealSpan(
    long StartTimeMs,
    long EndTimeMs,
    float Leading,
    float Trailing);

internal readonly record struct FocusedLineRevealSample(float Progress, float Position);

/// <summary>
/// Maps timed token edges onto one visual line without restarting the reveal at every token.
/// The implementation uses a monotone cubic Hermite curve so it cannot overshoot or erase
/// already highlighted content.
/// </summary>
internal sealed class FocusedLineRevealTimeline
{
    private readonly Anchor[] _anchors;
    private readonly float[] _tangents;
    private readonly FocusedLineRevealSpan[] _spans;

    private FocusedLineRevealTimeline(
        float left,
        float right,
        bool isRightToLeft,
        Anchor[] anchors,
        float[] tangents,
        FocusedLineRevealSpan[] spans,
        bool isSpatiallyMonotone)
    {
        Left = left;
        Right = right;
        IsRightToLeft = isRightToLeft;
        _anchors = anchors;
        _tangents = tangents;
        _spans = spans;
        IsSpatiallyMonotone = isSpatiallyMonotone;
    }

    public float Left { get; }
    public float Right { get; }
    public float Width => Math.Max(Right - Left, 0.001f);
    public bool IsRightToLeft { get; }
    public bool IsSpatiallyMonotone { get; }
    public IReadOnlyList<FocusedLineRevealSpan> Spans => _spans;
    public long StartTimeMs => _anchors[0].TimeMs;
    public long EndTimeMs => _anchors[^1].TimeMs;

    public static FocusedLineRevealTimeline Create(
        IReadOnlyList<FocusedLineRevealSpan> spans,
        float left,
        float right,
        bool isRightToLeft)
    {
        left = Math.Min(left, right);
        right = Math.Max(left + 0.001f, right);
        if (spans.Count == 0)
        {
            var emptyAnchors = new[] { new Anchor(0, 0), new Anchor(1, 1) };
            return new FocusedLineRevealTimeline(left, right, isRightToLeft,
                emptyAnchors, new float[emptyAnchors.Length], [], true);
        }

        var raw = new List<Anchor>(spans.Count * 2);
        var isSpatiallyMonotone = true;
        var previousTrailing = 0f;
        for (var index = 0; index < spans.Count; index++)
        {
            var span = spans[index];
            var leading = Normalize(span.Leading, left, right, isRightToLeft);
            var trailing = Normalize(span.Trailing, left, right, isRightToLeft);
            if (trailing + 0.0001f < leading) isSpatiallyMonotone = false;
            if (trailing < leading) (leading, trailing) = (trailing, leading);
            if (index > 0 && leading + 0.0001f < previousTrailing) isSpatiallyMonotone = false;
            previousTrailing = Math.Max(previousTrailing, trailing);
            raw.Add(new Anchor(span.StartTimeMs, leading));
            raw.Add(new Anchor(Math.Max(span.StartTimeMs, span.EndTimeMs), trailing));
        }

        raw.Sort(static (first, second) => first.TimeMs.CompareTo(second.TimeMs));
        var merged = new List<Anchor>(raw.Count);
        for (var index = 0; index < raw.Count; index++)
        {
            var anchor = raw[index];
            if (merged.Count > 0 && merged[^1].TimeMs == anchor.TimeMs)
            {
                // Two spatial edges cannot both be crossed at one timestamp. Keep the
                // furthest edge so the timeline remains monotone and catches up once.
                merged[^1] = merged[^1] with { Progress = Math.Max(merged[^1].Progress, anchor.Progress) };
                continue;
            }

            merged.Add(anchor);
        }

        var monotone = new List<Anchor>(merged.Count + 2);
        var progress = 0f;
        for (var index = 0; index < merged.Count; index++)
        {
            progress = Math.Max(progress, merged[index].Progress);
            monotone.Add(merged[index] with { Progress = progress });
        }

        if (monotone.Count == 1)
            monotone.Add(new Anchor(monotone[0].TimeMs + 1, monotone[0].Progress));

        var anchors = monotone.ToArray();
        return new FocusedLineRevealTimeline(left, right, isRightToLeft,
            anchors, CreateTangents(anchors), spans.ToArray(), isSpatiallyMonotone);
    }

    public FocusedLineRevealSample Sample(long timeMs)
    {
        if (timeMs <= StartTimeMs) return ToSample(0);
        if (timeMs >= EndTimeMs) return ToSample(1);

        var upper = Array.BinarySearch(_anchors, new Anchor(timeMs, 0), AnchorTimeComparer.Instance);
        if (upper >= 0) return ToSample(_anchors[upper].Progress);
        upper = ~upper;
        var lower = upper - 1;
        var first = _anchors[lower];
        var second = _anchors[upper];
        var duration = second.TimeMs - first.TimeMs;
        if (duration <= 0) return ToSample(second.Progress);

        var t = (timeMs - first.TimeMs) / (float)duration;
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = 2 * t3 - 3 * t2 + 1;
        var h10 = t3 - 2 * t2 + t;
        var h01 = -2 * t3 + 3 * t2;
        var h11 = t3 - t2;
        var progress = h00 * first.Progress + h10 * duration * _tangents[lower] +
                       h01 * second.Progress + h11 * duration * _tangents[upper];
        return ToSample(Math.Clamp(progress, first.Progress, second.Progress));
    }

    public FocusedLineRevealSample SampleSpan(FocusedLineRevealSpan span, long timeMs)
    {
        var duration = span.EndTimeMs - span.StartTimeMs;
        var progress = duration <= 0
            ? timeMs >= span.StartTimeMs ? 1 : 0
            : Math.Clamp((timeMs - span.StartTimeMs) / (float)duration, 0, 1);
        progress = progress * progress * (3 - 2 * progress);
        return new FocusedLineRevealSample(progress,
            span.Leading + (span.Trailing - span.Leading) * progress);
    }

    private FocusedLineRevealSample ToSample(float progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var scanPosition = Left + Width * progress;
        var position = IsRightToLeft ? Right - (scanPosition - Left) : scanPosition;
        return new FocusedLineRevealSample(progress, position);
    }

    private static float Normalize(float value, float left, float right, bool isRightToLeft)
    {
        var physical = Math.Clamp(value, left, right);
        return isRightToLeft
            ? (right - physical) / Math.Max(right - left, 0.001f)
            : (physical - left) / Math.Max(right - left, 0.001f);
    }

    private static float[] CreateTangents(IReadOnlyList<Anchor> anchors)
    {
        var tangents = new float[anchors.Count];
        if (anchors.Count <= 2) return tangents;

        var slopes = new float[anchors.Count - 1];
        for (var index = 0; index < slopes.Length; index++)
        {
            var duration = anchors[index + 1].TimeMs - anchors[index].TimeMs;
            slopes[index] = duration <= 0
                ? 0
                : (anchors[index + 1].Progress - anchors[index].Progress) / duration;
        }

        // A zero endpoint tangent makes the line fade in and settle without the first
        // or last rendered frame receiving a disproportionate opacity jump.
        tangents[0] = 0;
        tangents[^1] = 0;
        for (var index = 1; index < tangents.Length - 1; index++)
        {
            var previous = slopes[index - 1];
            var next = slopes[index];
            if (previous <= 0 || next <= 0)
            {
                tangents[index] = 0;
                continue;
            }

            var previousDuration = anchors[index].TimeMs - anchors[index - 1].TimeMs;
            var nextDuration = anchors[index + 1].TimeMs - anchors[index].TimeMs;
            var firstWeight = 2 * nextDuration + previousDuration;
            var secondWeight = nextDuration + 2 * previousDuration;
            tangents[index] = (firstWeight + secondWeight) /
                              (firstWeight / previous + secondWeight / next);
        }

        return tangents;
    }

    private readonly record struct Anchor(long TimeMs, float Progress);

    private sealed class AnchorTimeComparer : IComparer<Anchor>
    {
        public static AnchorTimeComparer Instance { get; } = new();
        public int Compare(Anchor x, Anchor y) => x.TimeMs.CompareTo(y.TimeMs);
    }
}
