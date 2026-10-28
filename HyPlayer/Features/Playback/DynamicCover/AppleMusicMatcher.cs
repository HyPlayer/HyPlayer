using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyPlayer.Features.Playback.DynamicCover;

internal sealed record DynamicCoverQuery(string Title, IReadOnlyList<string> Artists, string AlbumName, long DurationMs);

internal sealed record AppleMusicSongCandidate(
    string Id,
    string Name,
    string ArtistName,
    string AlbumName,
    long DurationMs,
    string? Isrc);

internal sealed record AppleMusicAlbumCandidate(string Id, string Name, string? VideoUrl, string? PreviewUrl);

internal static partial class AppleMusicMatcher
{
    private const int MinimumSongScore = 7;

    [GeneratedRegex(@"\s*[\(\[（【][^\)\]）】]*[\)\]）】]")]
    private static partial Regex BracketRegex();

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

    public static int ScoreSong(DynamicCoverQuery query, AppleMusicSongCandidate candidate)
    {
        int score;
        if (Normalize(query.Title) == Normalize(candidate.Name)) score = 4;
        else if (Core(query.Title).Length > 0 && Core(query.Title) == Core(candidate.Name)) score = 2;
        else return -1;

        var artist = Normalize(candidate.ArtistName);
        var artistMatched = query.Artists
            .Select(Normalize)
            .Any(name => name.Length > 0 && artist.Length > 0 && (artist.Contains(name) || name.Contains(artist)));
        if (!artistMatched) return -1;
        score += 3;

        if (query.DurationMs > 0 && candidate.DurationMs > 0)
        {
            var difference = Math.Abs(query.DurationMs - candidate.DurationMs);
            if (difference > 10_000) return -1;
            if (difference <= 2_000) score += 3;
            else if (difference <= 5_000) score += 1;
        }

        if (Core(query.AlbumName).Length > 0 && Core(query.AlbumName) == Core(candidate.AlbumName)) score += 2;
        return score;
    }

    public static AppleMusicSongCandidate? PickSong(DynamicCoverQuery query, IEnumerable<AppleMusicSongCandidate> candidates)
    {
        AppleMusicSongCandidate? best = null;
        var bestScore = MinimumSongScore - 1;
        foreach (var candidate in candidates)
        {
            var score = ScoreSong(query, candidate);
            if (score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }

        return best;
    }

    public static AppleMusicAlbumCandidate? PickAlbum(
        DynamicCoverQuery query,
        IEnumerable<AppleMusicAlbumCandidate> albums,
        string matchedSongAlbumName)
    {
        var animated = albums.Where(album => !string.IsNullOrEmpty(album.VideoUrl)).ToList();
        var albumName = Core(query.AlbumName);
        if (albumName.Length > 0 && animated.FirstOrDefault(album => Core(album.Name) == albumName) is { } named)
            return named;
        var songAlbumName = Core(matchedSongAlbumName);
        return songAlbumName.Length > 0 ? animated.FirstOrDefault(album => Core(album.Name) == songAlbumName) : null;
    }

    public static IReadOnlyList<AppleMusicSongCandidate> ParseSearchSongs(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("songs", out var songs) ||
            !songs.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return [];
        return data.EnumerateArray().Select(ReadSong).OfType<AppleMusicSongCandidate>().ToList();
    }

    public static IReadOnlyList<AppleMusicAlbumCandidate> ParseIsrcAlbums(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];
        var albums = new List<AppleMusicAlbumCandidate>();
        foreach (var song in data.EnumerateArray())
        {
            if (!song.TryGetProperty("relationships", out var relationships) ||
                !relationships.TryGetProperty("albums", out var albumRelation) ||
                !albumRelation.TryGetProperty("data", out var albumData) ||
                albumData.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var album in albumData.EnumerateArray())
                if (ReadAlbum(album) is { } candidate && albums.All(existing => existing.Id != candidate.Id))
                    albums.Add(candidate);
        }

        return albums;
    }

    private static AppleMusicSongCandidate? ReadSong(JsonElement song)
    {
        if (!song.TryGetProperty("attributes", out var attributes)) return null;
        var id = String(song, "id");
        if (string.IsNullOrEmpty(id)) return null;
        return new AppleMusicSongCandidate(
            id,
            String(attributes, "name") ?? string.Empty,
            String(attributes, "artistName") ?? string.Empty,
            String(attributes, "albumName") ?? string.Empty,
            attributes.TryGetProperty("durationInMillis", out var duration) && duration.ValueKind == JsonValueKind.Number
                ? duration.GetInt64()
                : 0,
            String(attributes, "isrc"));
    }

    private static AppleMusicAlbumCandidate? ReadAlbum(JsonElement album)
    {
        var id = String(album, "id");
        if (string.IsNullOrEmpty(id) || !album.TryGetProperty("attributes", out var attributes)) return null;
        string? video = null;
        string? preview = null;
        if (attributes.TryGetProperty("editorialVideo", out var editorialVideo))
            foreach (var key in (string[])["motionDetailSquare", "motionSquareVideo1x1"])
            {
                if (!editorialVideo.TryGetProperty(key, out var motion)) continue;
                video = String(motion, "video");
                if (motion.TryGetProperty("previewFrame", out var frame)) preview = String(frame, "url");
                if (!string.IsNullOrEmpty(video)) break;
            }

        return new AppleMusicAlbumCandidate(id, String(attributes, "name") ?? string.Empty, video, preview);
    }

    private static string? String(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
