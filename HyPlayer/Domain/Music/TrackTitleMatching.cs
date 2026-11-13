using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HyPlayer.Domain.Music;

internal static partial class TrackTitleMatching
{
    private static readonly string[] NeutralMarkers =
    [
        "feat", "ft.", "deluxe", "edition", "remaster", "expanded", "bonus", "explicit", "clean", "anniversary"
    ];

    [GeneratedRegex(@"\s*[\(\[（【][^\)\]）】]*[\)\]）】]")]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"[\(\[（【](?<content>[^\)\]）】]*)[\)\]）】]")]
    private static partial Regex BracketContentRegex();

    [GeneratedRegex(@"\s+-\s+(single|ep)$")]
    private static partial Regex ReleaseSuffixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('“', '"')
            .Replace('”', '"');
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    public static string Core(string? value)
    {
        var text = ReleaseSuffixRegex().Replace(Normalize(value), string.Empty);
        return WhitespaceRegex().Replace(BracketRegex().Replace(text, string.Empty), " ").Trim();
    }

    public static IReadOnlySet<string> VersionMarkers(string? value)
    {
        return BracketContentRegex().Matches(Normalize(value))
            .Select(match => match.Groups["content"].Value.Trim())
            .Where(content => content.Length > 0 && content is not ("single" or "ep") && !content.StartsWith("with ") &&
                              !NeutralMarkers.Any(content.Contains))
            .ToHashSet();
    }

    public static bool SameVersion(string? left, string? right)
    {
        return VersionMarkers(left).SetEquals(VersionMarkers(right));
    }

    public static bool SameTitle(string? left, string? right)
    {
        var core = Core(left);
        return core.Length > 0 && core == Core(right) && SameVersion(left, right);
    }

    public static bool SameArtist(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        return a.Length >= 2 && b.Length >= 2 && (a.Contains(b) || b.Contains(a));
    }
}
