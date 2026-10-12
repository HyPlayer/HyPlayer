using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class VocalActivity
{
    public const double Rate = 20;

    public VocalActivity(double start, double[] values)
    {
        Start = start;
        Values = values;
    }

    public double Start { get; }
    public double[] Values { get; }
    public double End => Start + Values.Length / Rate;

    public List<(double Start, double End)> Regions(double enter, double exit, double minimumSeconds = 0.5)
    {
        var regions = new List<(double Start, double End)>();
        var active = false;
        var regionStart = 0;
        for (var i = 0; i <= Values.Length; i++)
        {
            var value = i < Values.Length ? Values[i] : 0;
            if (!active && value >= enter)
            {
                active = true;
                regionStart = i;
            }
            else if (active && value < exit)
            {
                active = false;
                regions.Add((Start + regionStart / Rate, Start + i / Rate));
            }
        }
        var merged = new List<(double Start, double End)>();
        foreach (var region in regions)
        {
            if (merged.Count > 0 && region.Start - merged[^1].End < 0.8)
                merged[^1] = (merged[^1].Start, region.End);
            else
                merged.Add(region);
        }
        merged.RemoveAll(r => r.End - r.Start < minimumSeconds);
        return merged;
    }

    public static List<(double Start, double End)> Fuse(IReadOnlyList<(double Start, double End)> lyric, VocalActivity? activity)
    {
        if (activity is null) return lyric.ToList();
        var result = new List<(double Start, double End)>();
        foreach (var (start, end) in lyric)
        {
            if (start < activity.Start) result.Add((start, Math.Min(end, activity.Start)));
            if (end > activity.End) result.Add((Math.Max(start, activity.End), end));
        }
        if (lyric.Count == 0)
        {
            result.AddRange(activity.Regions(0.2, 0.1));
        }
        else
        {
            foreach (var region in activity.Regions(0.12, 0.06))
            {
                foreach (var (start, end) in lyric)
                {
                    var from = Math.Max(region.Start, start - 0.6);
                    var to = Math.Min(region.End, end + 0.5);
                    if (to - from >= 0.3) result.Add((from, to));
                }
            }
            result.AddRange(activity.Regions(0.3, 0.15, 1));
        }
        result.Sort((x, y) => x.Start.CompareTo(y.Start));
        var merged = new List<(double Start, double End)>();
        foreach (var region in result)
        {
            if (merged.Count > 0 && region.Start - merged[^1].End < 0.3)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, region.End));
            else
                merged.Add(region);
        }
        return merged;
    }
}
