using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal sealed record LyricSearchQuery(string Title, IReadOnlyList<string> Artists, long DurationMs)
{
    public string PrimaryArtist => Artists[0];
}

internal static partial class LyricSearchQueryBuilder
{
    private const long MaxProgramDurationMs = 15 * 60 * 1000;

    private static readonly string[] PlaceholderArtists = ["未知歌手", "未知歌手 - 播放后加载", "未知艺术家"];

    [GeneratedRegex(@"\.(flac|mp3|m4a|wav|ape|ogg|opus|aac|wma|ncm)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(@"\s*[+\-]\s*\d+\s*key$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyShiftRegex();

    [GeneratedRegex(@"\s*(?:;|；|/|、|&|,|，|\s(?:feat\.?|ft\.?)\s)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparatorRegex();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]|【[^】]*】)\s*")]
    private static partial Regex LeadingTagRegex();

    [GeneratedRegex(@"[【\[(（]\s*\d+(?:\.\d+)?\s*[x×]\s*[】\])）]|\s\d+(?:\.\d+)?\s*[x×]$|\s\d\.\d{1,2}$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpeedMarkerRegex();

    [GeneratedRegex(@"Artists?\s*[:：]\s*(?<artist>[^\r\n]+?)(?=\s+(?:BPM|Mapper)\b|\r|\n|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionArtistRegex();

    public static LyricSearchQuery? FromSong(string? name, IEnumerable<string>? creators, long durationMs)
    {
        var title = CleanTitle(name);
        var artists = SplitArtists(creators);
        return title.Length == 0 || artists.Count == 0 ? null : new LyricSearchQuery(title, artists, durationMs);
    }

    public static LyricSearchQuery? FromProgram(string? name, string? description, long durationMs)
    {
        if (durationMs <= 0 || durationMs > MaxProgramDurationMs || string.IsNullOrWhiteSpace(name)) return null;
        var title = name.Trim();
        for (var match = LeadingTagRegex().Match(title); match.Success; match = LeadingTagRegex().Match(title))
            title = title[match.Length..];
        title = title.Trim();
        if (title.Length == 0 || SpeedMarkerRegex().IsMatch(title)) return null;

        if (!string.IsNullOrWhiteSpace(description) &&
            DescriptionArtistRegex().Match(description) is { Success: true } artistMatch)
            return FromSong(title, [artistMatch.Groups["artist"].Value], durationMs);

        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        return separator <= 0 ? null : FromSong(title[(separator + 3)..], [title[..separator]], durationMs);
    }

    public static string CleanTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var title = ExtensionRegex().Replace(name.Trim(), string.Empty);
        return KeyShiftRegex().Replace(title, string.Empty).Trim();
    }

    public static IReadOnlyList<string> SplitArtists(IEnumerable<string>? creators)
    {
        if (creators is null) return [];
        return creators
            .SelectMany(creator => ArtistSeparatorRegex().Split(creator ?? string.Empty))
            .Select(artist => artist.Trim())
            .Where(artist => artist.Length > 0 && !PlaceholderArtists.Contains(artist))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
