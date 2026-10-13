#nullable enable

using System;
using System.Collections.Generic;

namespace HyPlayer.LyricRenderer.Text;

internal readonly record struct FocusedLineRevealSpan(
    long StartTimeMs,
    long EndTimeMs,
    float Leading,
    float Trailing);

internal readonly record struct FocusedLineRevealSample(float Progress, float Position);

internal sealed class FocusedLineRevealTimeline
{
    private const float SpatialTolerance = 0.0001f;

    private readonly long[] _times;
    private readonly float[] _progress;
    private readonly FocusedLineRevealSpan[] _spans;

    private FocusedLineRevealTimeline(
        float left,
        float right,
        bool isRightToLeft,
        long[] times,
        float[] progress,
        FocusedLineRevealSpan[] spans,
        bool isSpatiallyMonotone)
    {
        Left = left;
        Right = right;
        IsRightToLeft = isRightToLeft;
        _times = times;
        _progress = progress;
        _spans = spans;
        IsSpatiallyMonotone = isSpatiallyMonotone;
    }

    public float Left { get; }
    public float Right { get; }
    public float Width => Math.Max(Right - Left, 0.001f);
    public bool IsRightToLeft { get; }
    public bool IsSpatiallyMonotone { get; }
    public IReadOnlyList<FocusedLineRevealSpan> Spans => _spans;
    public long StartTimeMs => _times[0];
    public long EndTimeMs => _times[^1];

    public static FocusedLineRevealTimeline Create(
        IReadOnlyList<FocusedLineRevealSpan> spans,
        float left,
        float right,
        bool isRightToLeft)
    {
        left = Math.Min(left, right);
        right = Math.Max(left + 0.001f, right);
        if (spans.Count == 0)
            return new FocusedLineRevealTimeline(left, right, isRightToLeft, [0, 1], [0, 1], [], true);

        var isSpatiallyMonotone = true;
        var previousLeading = 0f;
        var previousTrailing = 0f;
        var times = new List<long>(spans.Count * 2);
        var progress = new List<float>(spans.Count * 2);
        var edge = 0f;
        for (var index = 0; index < spans.Count; index++)
        {
            var span = spans[index];
            var leading = Normalize(span.Leading, left, right, isRightToLeft);
            var trailing = Normalize(span.Trailing, left, right, isRightToLeft);
            if (trailing + SpatialTolerance < leading) isSpatiallyMonotone = false;
            if (trailing < leading) (leading, trailing) = (trailing, leading);
            if (index > 0 && (leading + SpatialTolerance < previousLeading ||
                              trailing + SpatialTolerance < previousTrailing))
                isSpatiallyMonotone = false;
            previousLeading = Math.Max(previousLeading, leading);
            previousTrailing = Math.Max(previousTrailing, trailing);

            var trailingEdge = index == spans.Count - 1 ? 1 : Math.Max(edge, trailing);
            Append(times, progress, span.StartTimeMs, edge);
            Append(times, progress, Math.Max(span.StartTimeMs, span.EndTimeMs), trailingEdge);
            edge = trailingEdge;
        }

        if (progress[^1] < 1 || times.Count == 1)
        {
            times.Add(times[^1] + 1);
            progress.Add(1);
        }

        return new FocusedLineRevealTimeline(left, right, isRightToLeft,
            times.ToArray(), progress.ToArray(), [.. spans], isSpatiallyMonotone);
    }

    public FocusedLineRevealSample Sample(double timeMs)
    {
        var times = _times;
        if (timeMs <= times[0]) return ToSample(_progress[0]);
        var last = times.Length - 1;
        if (timeMs >= times[last]) return ToSample(_progress[last]);

        var lower = 0;
        var upper = last;
        while (upper - lower > 1)
        {
            var middle = (lower + upper) >> 1;
            if (times[middle] <= timeMs) lower = middle;
            else upper = middle;
        }

        var t = (timeMs - times[lower]) / (times[upper] - times[lower]);
        return ToSample((float)(_progress[lower] + (_progress[upper] - _progress[lower]) * t));
    }

    public FocusedLineRevealSample SampleSpan(FocusedLineRevealSpan span, double timeMs)
    {
        var duration = span.EndTimeMs - span.StartTimeMs;
        var progress = duration <= 0
            ? timeMs >= span.StartTimeMs ? 1f : 0f
            : (float)Math.Clamp((timeMs - span.StartTimeMs) / duration, 0, 1);
        return new FocusedLineRevealSample(progress,
            span.Leading + (span.Trailing - span.Leading) * progress);
    }

    private static void Append(List<long> times, List<float> progress, long time, float value)
    {
        if (times.Count == 0)
        {
            times.Add(time);
            progress.Add(value);
            return;
        }

        var last = times.Count - 1;
        var previousValue = progress[last];
        value = Math.Max(value, previousValue);
        if (time <= times[last])
        {
            if (value <= previousValue) return;
            if (last > 0) progress[last] = value;
            return;
        }

        if (value <= previousValue && last > 0 && progress[last - 1] >= previousValue)
        {
            times[last] = time;
            return;
        }

        times.Add(time);
        progress.Add(value);
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
}
