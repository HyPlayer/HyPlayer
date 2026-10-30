using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HyPlayer.Features.Playback.DynamicCover;

internal sealed record HlsVariant(Uri Uri, int Width, int Height, long Bandwidth, string Codecs)
{
    public bool IsAvc => Codecs.Split(',').Any(codec => codec.Trim().StartsWith("avc1", StringComparison.OrdinalIgnoreCase));
}

internal static class HlsPlaylist
{
    public static IReadOnlyList<HlsVariant> ParseVariants(string master, Uri baseUri)
    {
        var variants = new List<HlsVariant>();
        var lines = SplitLines(master);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
            var attributes = ParseAttributes(lines[i]["#EXT-X-STREAM-INF:".Length..]);
            var uriLine = lines.Skip(i + 1).FirstOrDefault(line => !line.StartsWith('#'));
            if (uriLine is null) continue;
            var (width, height) = ParseResolution(attributes.GetValueOrDefault("RESOLUTION"));
            long.TryParse(attributes.GetValueOrDefault("BANDWIDTH"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var bandwidth);
            variants.Add(new HlsVariant(new Uri(baseUri, uriLine), width, height, bandwidth,
                attributes.GetValueOrDefault("CODECS") ?? string.Empty));
        }

        return variants;
    }

    public static HlsVariant? SelectVariant(IReadOnlyList<HlsVariant> variants, int maxEdge)
    {
        var avc = variants.Where(variant => variant.IsAvc && variant.Width > 0).ToList();
        if (avc.Count == 0) return null;
        var fitting = avc.Where(variant => Math.Max(variant.Width, variant.Height) <= maxEdge).ToList();
        if (fitting.Count > 0)
            return fitting.OrderByDescending(variant => variant.Width * variant.Height)
                .ThenByDescending(variant => variant.Bandwidth)
                .First();
        return avc.OrderBy(variant => variant.Width * variant.Height)
            .ThenByDescending(variant => variant.Bandwidth)
            .First();
    }

    public static IReadOnlyList<Uri> ParseMediaFiles(string media, Uri baseUri)
    {
        var files = new List<Uri>();
        foreach (var line in SplitLines(media))
        {
            string? reference = null;
            if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
                reference = ParseAttributes(line["#EXT-X-MAP:".Length..]).GetValueOrDefault("URI");
            else if (!line.StartsWith('#'))
                reference = line;
            if (string.IsNullOrEmpty(reference)) continue;
            var uri = new Uri(baseUri, reference);
            if (!files.Contains(uri)) files.Add(uri);
        }

        return files;
    }

    private static List<string> SplitLines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            var equals = text.IndexOf('=', i);
            if (equals < 0) break;
            var name = text[i..equals].Trim();
            i = equals + 1;
            string value;
            if (i < text.Length && text[i] == '"')
            {
                var close = text.IndexOf('"', i + 1);
                if (close < 0) close = text.Length;
                value = text[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                var comma = text.IndexOf(',', i);
                if (comma < 0) comma = text.Length;
                value = text[i..comma].Trim();
                i = comma;
            }

            attributes[name] = value;
            if (i < text.Length && text[i] == ',') i++;
        }

        return attributes;
    }

    private static (int Width, int Height) ParseResolution(string? resolution)
    {
        if (string.IsNullOrEmpty(resolution)) return (0, 0);
        var parts = resolution.Split('x', 'X');
        if (parts.Length != 2) return (0, 0);
        int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width);
        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height);
        return (width, height);
    }
}
