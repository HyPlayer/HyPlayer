using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyPlayer.Domain.Lyrics;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal sealed record KrcLine(long StartMs, long DurationMs, IReadOnlyList<KrcWord> Words)
{
    public string Text => string.Concat(Words.Select(word => word.Text));
}

internal sealed record KrcWord(long OffsetMs, long DurationMs, string Text);

internal sealed record KrcDocument(
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<KrcLine> Lines,
    IReadOnlyList<string?> Translations,
    IReadOnlyList<string?> Romaji);

internal static partial class KrcLyricParser
{
    private static readonly byte[] Key =
        [0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69];

    private static readonly string[] TranslationNotices = ["著作权", "版权", "翻译由", "大模型", "仅供参考"];

    [GeneratedRegex(@"^\[(?<name>[A-Za-z_]+):(?<value>.*)\]$")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^\[(?<start>\d+),(?<duration>\d+)\](?<content>.*)$")]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"<(?<offset>\d+),(?<duration>\d+),\d+>(?<text>[^<]*)")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\p{IsCJKUnifiedIdeographs}")]
    private static partial Regex HanRegex();

    public static string Decrypt(byte[] content)
    {
        if (content.Length <= 4 || content[0] != 'k' || content[1] != 'r' || content[2] != 'c' || content[3] != '1')
            throw new InvalidDataException("Not a KRC payload.");
        var data = new byte[content.Length - 4];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(content[i + 4] ^ Key[i % Key.Length]);
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(zlib, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static KrcDocument Parse(string krc)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<KrcLine>();
        foreach (var rawLine in krc.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (LineRegex().Match(line) is { Success: true } lineMatch)
            {
                var start = long.Parse(lineMatch.Groups["start"].Value, CultureInfo.InvariantCulture);
                var duration = long.Parse(lineMatch.Groups["duration"].Value, CultureInfo.InvariantCulture);
                var content = lineMatch.Groups["content"].Value;
                var words = WordRegex().Matches(content)
                    .Select(word => new KrcWord(
                        long.Parse(word.Groups["offset"].Value, CultureInfo.InvariantCulture),
                        long.Parse(word.Groups["duration"].Value, CultureInfo.InvariantCulture),
                        word.Groups["text"].Value))
                    .ToList();
                if (words.Count == 0) words.Add(new KrcWord(0, duration, content));
                lines.Add(new KrcLine(start, duration, words));
                continue;
            }

            if (TagRegex().Match(line) is { Success: true } tagMatch)
                tags[tagMatch.Groups["name"].Value] = tagMatch.Groups["value"].Value;
        }

        var translations = new string?[lines.Count];
        var romaji = new string?[lines.Count];
        if (tags.TryGetValue("language", out var language) && !string.IsNullOrWhiteSpace(language))
            ReadLanguages(language.Trim(), lines, translations, romaji);

        return new KrcDocument(tags, lines, translations, romaji);
    }

    private static void ReadLanguages(string base64, List<KrcLine> lines, string?[] translations, string?[] romaji)
    {
        using var document = JsonDocument.Parse(Convert.FromBase64String(base64));
        if (!document.RootElement.TryGetProperty("content", out var contents) ||
            contents.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in contents.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeElement) ||
                !typeElement.TryGetInt32(out var type) ||
                !item.TryGetProperty("lyricContent", out var lyricContent) ||
                lyricContent.ValueKind != JsonValueKind.Array)
                continue;

            var rows = lyricContent.EnumerateArray()
                .Select(row => row.ValueKind == JsonValueKind.Array
                    ? row.EnumerateArray().Select(cell => cell.ValueKind == JsonValueKind.String ? cell.GetString() : null)
                        .ToList()
                    : [])
                .ToList();
            if (type == 1 && rows.Count == lines.Count)
                for (var i = 0; i < rows.Count; i++)
                    translations[i] = CleanTranslation(rows[i].FirstOrDefault());
            else if (type == 0)
                ReadRomaji(rows, lines, romaji);
        }
    }

    private static void ReadRomaji(List<List<string?>> rows, List<KrcLine> lines, string?[] romaji)
    {
        var values = new string?[lines.Count];
        if (rows.Count == lines.Count)
        {
            for (var i = 0; i < rows.Count; i++) values[i] = JoinRomaji(rows[i]);
        }
        else
        {
            var row = 0;
            for (var i = 0; i < lines.Count && row < rows.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i].Text)) continue;
                values[i] = JoinRomaji(rows[row++]);
            }
        }

        if (values.Any(value => value is not null && HanRegex().IsMatch(value))) return;
        Array.Copy(values, romaji, values.Length);
    }

    private static string? JoinRomaji(List<string?> words)
    {
        var text = string.Join(' ', words.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word!.Trim()));
        return text.Length == 0 ? null : text;
    }

    private static string? CleanTranslation(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || value == "//") return null;
        return TranslationNotices.Any(notice => value.Contains(notice, StringComparison.Ordinal)) ? null : value;
    }

    public static PureLyricInfo ToLyricInfo(KrcDocument document, LyricSearchQuery query, string sourceUri)
    {
        var info = ToKaraokeInfo(document, query, sourceUri);
        var lines = document.Lines.Skip(IsHeaderLine(document, query) ? 1 : 0)
            .Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToList();
        if (lines.Count > 0 && lines.Count(line => line.Words.Count > 1) * 2 >= lines.Count) return info;
        return new PureLyricInfo
        {
            PureLyrics = info.PureLyrics,
            TrLyrics = info.TrLyrics,
            NeteaseRomaji = info.NeteaseRomaji,
            LyricMetadata = info.LyricMetadata
        };
    }

    private static KaraokLyricInfo ToKaraokeInfo(KrcDocument document, LyricSearchQuery query, string sourceUri)
    {
        var skip = IsHeaderLine(document, query) ? 1 : 0;
        var karaoke = new StringBuilder();
        var pure = new StringBuilder();
        var translation = new StringBuilder();
        var romaji = new StringBuilder();
        for (var i = skip; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            karaoke.Append('[').Append(line.StartMs).Append(',').Append(line.DurationMs).Append(']');
            foreach (var word in line.Words)
                karaoke.Append('(').Append(line.StartMs + word.OffsetMs).Append(',').Append(word.DurationMs)
                    .Append(",0)").Append(word.Text);
            karaoke.Append('\n');

            var stamp = Timestamp(line.StartMs);
            pure.Append(stamp).Append(line.Text).Append('\n');
            if (document.Translations[i] is { } tr) translation.Append(stamp).Append(tr).Append('\n');
            if (document.Romaji[i] is { } ro) romaji.Append(stamp).Append(ro).Append('\n');
        }

        var translationText = translation.ToString().TrimEnd('\n');
        var romajiText = romaji.ToString().TrimEnd('\n');
        return new KaraokLyricInfo
        {
            KaraokLyric = karaoke.ToString().TrimEnd('\n'),
            PureLyrics = pure.ToString().TrimEnd('\n'),
            TrLyrics = translationText,
            YrTrLyrics = translationText,
            NeteaseRomaji = romajiText,
            YrNeteaseRomaji = romajiText,
            LyricMetadata =
            [
                new LyricInfoMetadata
                {
                    Key = "source",
                    Value = KugouLyricClient.SourceName,
                    DisplayName = "歌词来源",
                    ActionUri = sourceUri
                }
            ]
        };
    }

    private static bool IsHeaderLine(KrcDocument document, LyricSearchQuery query)
    {
        if (document.Lines.Count == 0) return false;
        var text = document.Lines[0].Text.Trim();
        if (!text.Contains(" - ", StringComparison.Ordinal)) return false;
        var title = document.Tags.TryGetValue("ti", out var ti) && !string.IsNullOrWhiteSpace(ti) ? ti : query.Title;
        return ContainsIgnoreCase(text, query.Title) || ContainsIgnoreCase(text, TrimBrackets(title));
    }

    private static string TrimBrackets(string title)
    {
        var index = title.IndexOfAny(['(', '（', '[', '【']);
        return (index > 0 ? title[..index] : title).Trim();
    }

    private static bool ContainsIgnoreCase(string text, string value)
    {
        return value.Length > 0 && text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string Timestamp(long ms)
    {
        var time = TimeSpan.FromMilliseconds(ms);
        return $"[{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}]";
    }
}
