using System;
using System.Diagnostics;
using System.Text;
using HyPlayer.LyricRenderer;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

[Flags]
public enum FrameMarkers
{
    None = 0,
    LineChange = 1,
    Layout = 2,
    BlurCache = 4,
    Seek = 8,
    TrackChange = 16,
    Transition = 32
}

public static class FrameMarkerText
{
    private static readonly string[] Names = ["换行", "排版", "模糊缓存", "跳转", "切歌", "过渡"];
    private static readonly string[] Descriptions = CreateDescriptions();

    public static FrameMarkers From(LyricFrameActivity activity, bool transitioning, bool trackChanged)
    {
        var markers = (FrameMarkers)((int)activity & 15);
        if (trackChanged) markers |= FrameMarkers.TrackChange;
        if (transitioning) markers |= FrameMarkers.Transition;
        return markers;
    }

    public static string Describe(this FrameMarkers markers) => Descriptions[(int)markers & 63];

    private static string[] CreateDescriptions()
    {
        var result = new string[64];
        var builder = new StringBuilder();
        for (var value = 0; value < result.Length; value++)
        {
            builder.Clear();
            for (var bit = 0; bit < Names.Length; bit++)
            {
                if ((value & 1 << bit) == 0) continue;
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(Names[bit]);
            }

            result[value] = builder.Length == 0 ? "无" : builder.ToString();
        }

        return result;
    }
}

public readonly record struct FrameStatisticsSummary(
    double LongestInterval,
    double LongestDraw,
    int LongFrames,
    int Frames,
    double AllocatedPerFrame,
    int Generation0,
    int Generation1,
    int Generation2,
    double PauseMilliseconds,
    FrameMarkers WorstMarkers);

public sealed class FrameStatisticsWindow
{
    private readonly Bucket[] _buckets;
    private int _head;
    private bool _started;

    public FrameStatisticsWindow(int seconds)
    {
        _buckets = new Bucket[Math.Max(1, seconds)];
    }

    public void Reset(long timestamp)
    {
        Array.Clear(_buckets);
        _head = 0;
        _started = true;
        Begin(ref _buckets[0], timestamp);
    }

    public void Add(long timestamp, double intervalMilliseconds, double drawMilliseconds, bool isLong,
        long allocatedBytes, FrameMarkers markers)
    {
        if (!_started) Reset(timestamp);
        if (timestamp - _buckets[_head].Start >= Stopwatch.Frequency)
        {
            _head = (_head + 1) % _buckets.Length;
            Begin(ref _buckets[_head], timestamp);
        }

        ref var bucket = ref _buckets[_head];
        bucket.Frames++;
        bucket.Allocated += Math.Max(0, allocatedBytes);
        if (isLong) bucket.LongFrames++;
        if (drawMilliseconds > bucket.LongestDraw) bucket.LongestDraw = drawMilliseconds;
        if (intervalMilliseconds <= bucket.LongestInterval) return;
        bucket.LongestInterval = intervalMilliseconds;
        bucket.Markers = markers;
    }

    public FrameStatisticsSummary Summarize()
    {
        var longestInterval = 0.0;
        var longestDraw = 0.0;
        var longFrames = 0;
        var frames = 0;
        long allocated = 0;
        var markers = FrameMarkers.None;
        var oldest = -1;
        for (var index = 0; index < _buckets.Length; index++)
        {
            ref var bucket = ref _buckets[index];
            if (!bucket.Used) continue;
            if (oldest < 0 || bucket.Start < _buckets[oldest].Start) oldest = index;
            frames += bucket.Frames;
            longFrames += bucket.LongFrames;
            allocated += bucket.Allocated;
            longestDraw = Math.Max(longestDraw, bucket.LongestDraw);
            if (bucket.LongestInterval <= longestInterval) continue;
            longestInterval = bucket.LongestInterval;
            markers = bucket.Markers;
        }

        if (oldest < 0) return default;
        ref var first = ref _buckets[oldest];
        return new FrameStatisticsSummary(longestInterval, longestDraw, longFrames, frames,
            frames > 0 ? allocated / (double)frames : 0,
            GC.CollectionCount(0) - first.Generation0,
            GC.CollectionCount(1) - first.Generation1,
            GC.CollectionCount(2) - first.Generation2,
            (GC.GetTotalPauseDuration().Ticks - first.PauseTicks) / (double)TimeSpan.TicksPerMillisecond,
            markers);
    }

    private static void Begin(ref Bucket bucket, long timestamp)
    {
        bucket = default;
        bucket.Used = true;
        bucket.Start = timestamp;
        bucket.Generation0 = GC.CollectionCount(0);
        bucket.Generation1 = GC.CollectionCount(1);
        bucket.Generation2 = GC.CollectionCount(2);
        bucket.PauseTicks = GC.GetTotalPauseDuration().Ticks;
    }

    private struct Bucket
    {
        public bool Used;
        public long Start;
        public double LongestInterval;
        public double LongestDraw;
        public int LongFrames;
        public int Frames;
        public long Allocated;
        public FrameMarkers Markers;
        public int Generation0;
        public int Generation1;
        public int Generation2;
        public long PauseTicks;
    }
}
