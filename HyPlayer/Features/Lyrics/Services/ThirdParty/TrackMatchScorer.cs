using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HyPlayer.Domain.Music;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal enum TrackMatchLevel
{
    NoMatch = 0,
    VeryLow = 10,
    Low = 30,
    Medium = 70,
    PrettyHigh = 90,
    High = 95,
    VeryHigh = 99,
    Perfect = 100
}

internal static partial class TrackMatchScorer
{
    public const TrackMatchLevel MinimumAccepted = TrackMatchLevel.High;

    private const int MaxFieldScore = 7;
    private const long MaxDurationDifferenceMs = 3_500;

    private static readonly string[] VersionKeywords =
    [
        "live", "remix", "mix", "inst", "伴奏", "纯音乐", "acoustic", "piano", "钢琴", "ver", "版", "cover", "翻唱",
        "demo", "edit", "radio", "extended", "sped up", "slowed", "现场", "演唱会", "karaoke", "tv size", "short",
        "dj", "remake", "重制", "伴唱", "off vocal", "orchestra", "unplugged", "a cappella", "nightcore"
    ];

    [GeneratedRegex(@"[\(\[](?<content>[^\)\]]*)[\)\]]")]
    private static partial Regex BracketContentRegex();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"[\s\p{P}\p{S}]+")]
    private static partial Regex SeparatorRegex();

    public static bool Accepts(TrackMatchLevel level) => level >= MinimumAccepted;

    public static TrackMatchLevel Score(LyricSearchQuery query, IEnumerable<string?>? titles,
        IEnumerable<string?>? artists, long? durationMs)
    {
        var name = titles?.Select(title => NameScore(query.Title, title)).Max();
        if (name is not > 0) return TrackMatchLevel.NoMatch;

        var duration = DurationScore(query.DurationMs, durationMs);
        if (duration == 0) return TrackMatchLevel.NoMatch;

        var artist = ArtistScore(query.Artists, LyricSearchQueryBuilder.SplitArtists(artists?.OfType<string>()));
        if (artist == 0) return TrackMatchLevel.NoMatch;

        var total = name.Value + (artist ?? 0) + (duration ?? 0);
        var available = MaxFieldScore * (1 + (artist is null ? 0 : 1) + (duration is null ? 0 : 1));
        return Level((double)total / available);
    }

    private static TrackMatchLevel Level(double ratio)
    {
        return ratio switch
        {
            > 0.83 => TrackMatchLevel.Perfect,
            > 0.75 => TrackMatchLevel.VeryHigh,
            > 0.67 => TrackMatchLevel.High,
            > 0.59 => TrackMatchLevel.PrettyHigh,
            > 0.43 => TrackMatchLevel.Medium,
            > 0.31 => TrackMatchLevel.Low,
            > 0.11 => TrackMatchLevel.VeryLow,
            _ => TrackMatchLevel.NoMatch
        };
    }

    internal static int? NameScore(string? left, string? right)
    {
        var a = NormalizeName(left);
        var b = NormalizeName(right);
        if (a.Length == 0 || b.Length == 0) return null;
        if (a == b) return 7;

        var coreA = Core(a);
        var coreB = Core(b);
        var markersA = Markers(a);
        var markersB = Markers(b);
        if (!markersA.SetEquals(markersB))
        {
            if (markersA.Concat(markersB).Except(markersA.Intersect(markersB)).Any(IsVersionMarker)) return 0;
            if (coreA == coreB) return 4;
        }
        else if (coreA == coreB)
        {
            return 6;
        }

        var compactA = SeparatorRegex().Replace(coreA, string.Empty);
        var compactB = SeparatorRegex().Replace(coreB, string.Empty);
        if (compactA.Length == 0 || compactB.Length == 0) return 0;
        if (compactA == compactB) return markersA.SetEquals(markersB) ? 6 : 4;

        if (compactA.Length == compactB.Length)
        {
            var same = compactA.Where((c, i) => c == compactB[i]).Count();
            var ratio = (double)same / compactA.Length;
            if ((compactA.Length >= 4 && ratio >= 0.8) || (compactA.Length is 2 or 3 && ratio >= 0.5)) return 5;
        }

        return Similarity(compactA, compactB) switch
        {
            > 0.9 => 6,
            > 0.8 => 5,
            > 0.68 => 4,
            > 0.55 => 2,
            _ => 0
        };
    }

    internal static int? ArtistScore(IReadOnlyList<string> query, IReadOnlyList<string> candidate)
    {
        if (query.Count == 0 || candidate.Count == 0) return null;
        var left = query.Select(NormalizeArtist).Where(a => a.Length > 0).Distinct().ToList();
        var right = candidate.Select(NormalizeArtist).Where(a => a.Length > 0).Distinct().ToList();
        if (left.Count == 0 || right.Count == 0) return null;

        var matched = left.Count(a => right.Any(b => SameArtist(a, b)));
        if (matched == 0) return 0;
        if (matched == left.Count && left.Count == right.Count) return 7;
        var smaller = Math.Min(left.Count, right.Count);
        if (matched >= smaller && Math.Abs(left.Count - right.Count) <= 1) return 6;
        if (matched >= smaller) return 5;
        return 4;
    }

    internal static int? DurationScore(long queryMs, long? candidateMs)
    {
        if (queryMs <= 0 || candidateMs is not > 0) return null;
        return Math.Abs(queryMs - candidateMs.Value) switch
        {
            0 => 7,
            < 300 => 6,
            < 700 => 5,
            < 1_500 => 4,
            < MaxDurationDifferenceMs => 2,
            _ => 0
        };
    }

    private static bool SameArtist(string a, string b)
    {
        if (a == b) return true;
        var compactA = SeparatorRegex().Replace(a, string.Empty);
        var compactB = SeparatorRegex().Replace(b, string.Empty);
        if (compactA.Length == 0 || compactB.Length == 0) return false;
        if (compactA == compactB) return true;
        return compactA.Length >= 2 && compactB.Length >= 2 &&
               (compactA.Contains(compactB, StringComparison.Ordinal) ||
                compactB.Contains(compactA, StringComparison.Ordinal));
    }

    private static string NormalizeName(string? value)
    {
        var text = TrackTitleMatching.Normalize(value)
            .Replace('【', '[')
            .Replace('】', ']')
            .Replace('「', '[')
            .Replace('」', ']')
            .Replace("acoustic version", "acoustic");
        return ChineseScript.ToSimplified(text.Replace(" (", "(").Replace("( ", "(").Replace(" )", ")"));
    }

    private static string NormalizeArtist(string? value)
    {
        return ChineseScript.ToSimplified(TrackTitleMatching.Normalize(value));
    }

    private static string Core(string normalized)
    {
        return BracketRegex().Replace(normalized, string.Empty).Replace(" - ", " ").Trim();
    }

    private static HashSet<string> Markers(string normalized)
    {
        var markers = BracketContentRegex().Matches(normalized)
            .Select(match => match.Groups["content"].Value.Trim())
            .Where(IsMeaningfulMarker)
            .ToHashSet();
        var dash = normalized.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            var suffix = normalized[(dash + 3)..].Trim();
            if (IsMeaningfulMarker(suffix) && IsVersionMarker(suffix)) markers.Add(suffix);
        }

        return markers;
    }

    private static bool IsMeaningfulMarker(string content)
    {
        return content.Length > 0 && content is not ("single" or "ep") &&
               !content.StartsWith("feat", StringComparison.Ordinal) &&
               !content.StartsWith("ft.", StringComparison.Ordinal) &&
               !content.StartsWith("with ", StringComparison.Ordinal) &&
               !(content.Contains("deluxe") || content.Contains("edition") || content.Contains("remaster") ||
                 content.Contains("explicit") || content.Contains("clean") || content.Contains("bonus") ||
                 content.Contains("anniversary") || content.Contains("expanded"));
    }

    private static bool IsVersionMarker(string marker)
    {
        return VersionKeywords.Any(keyword => marker.Contains(keyword, StringComparison.Ordinal)) ||
               marker.Any(char.IsDigit) && (marker.Contains('x') || marker.Contains('×') || marker.Contains("key"));
    }

    private static double Similarity(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
                current[j] = a[i - 1] == b[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            (previous, current) = (current, previous);
        }

        return (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }
}
