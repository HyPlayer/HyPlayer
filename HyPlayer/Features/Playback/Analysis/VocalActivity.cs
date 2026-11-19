using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.Features.Playback.Analysis.Dsp;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class VocalRegions
{
    public static readonly VocalRegions Empty = new(Array.Empty<(double Start, double End)>(), Array.Empty<(double Start, double End)>());

    public VocalRegions(IReadOnlyList<(double Start, double End)> vocals, IReadOnlyList<(double Start, double End)> leads)
    {
        Vocals = vocals;
        Leads = leads;
    }

    public IReadOnlyList<(double Start, double End)> Vocals { get; }
    public IReadOnlyList<(double Start, double End)> Leads { get; }
}

public sealed class VocalActivity
{
    public const double Rate = 20;
    private const double MaximumExtensionSeconds = 4;

    public VocalActivity(double start, double[] values)
    {
        Start = start;
        Values = values;
    }

    public double Start { get; }
    public double[] Values { get; }
    public string Analyzer { get; init; } = string.Empty;
    public bool IsLeadActivity => Analyzer == MidBandActivity.Id;
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

    public static VocalRegions Resolve(IReadOnlyList<(double Start, double End)> lyric, VocalActivity? activity)
    {
        if (activity is null || !activity.IsLeadActivity)
            return new VocalRegions(Fuse(lyric, activity), Array.Empty<(double Start, double End)>());

        var active = activity.Regions(MidBandActivity.EnterThreshold, MidBandActivity.ExitThreshold);
        if (lyric.Count == 0) return new VocalRegions(Array.Empty<(double Start, double End)>(), active);

        var strong = activity.Regions(MidBandActivity.StrongEnterThreshold, MidBandActivity.StrongExitThreshold, 1);
        var vocals = new List<(double Start, double End)>(lyric.Count);
        for (var i = 0; i < lyric.Count; i++)
        {
            var (start, end) = lyric[i];
            var extended = end;
            foreach (var region in strong)
                if (region.Start <= end && region.End > end && region.End - end <= MaximumExtensionSeconds)
                    extended = region.End;
            if (i + 1 < lyric.Count && lyric[i + 1].Start >= end) extended = Math.Min(extended, lyric[i + 1].Start);
            vocals.Add((start, extended));
        }

        var leads = new List<(double Start, double End)>();
        foreach (var region in active)
        {
            var from = region.Start;
            foreach (var (start, end) in vocals)
            {
                if (end <= from || start >= region.End) continue;
                if (start - from >= 0.5) leads.Add((from, start));
                from = Math.Max(from, end);
            }
            if (region.End - from >= 0.5) leads.Add((from, region.End));
        }
        return new VocalRegions(vocals, leads);
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
