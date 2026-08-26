using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ALRC.Abstraction;
using Kawazu;
using Lyricify.Lyrics.Helpers.General;
using Lyricify.Lyrics.Models;
using LyricifyLrcParser = Lyricify.Lyrics.Parsers.LrcParser;
using LyricifyYrcParser = Lyricify.Lyrics.Parsers.YrcParser;

namespace HyPlayer.Domain.Lyrics;

public static class Utils
{
    public static List<SongLyric> ConvertPureLyric(string lyricAllText)
    {
        return string.IsNullOrWhiteSpace(lyricAllText)
            ? []
            : ParseLrc(lyricAllText);
    }

    public static void ConvertTranslation(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyricAllText) || lyrics.Count == 0) return;

        ApplyAuxiliaryLyrics(ParseLrc(lyricAllText), lyrics, static (lyric, text) => lyric.Translation = text);
    }

    public static void ConvertYrcTranslation(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics,
        bool migrateLyrics = false)
    {
        if (lyrics.Count == 0) return;
        if (string.IsNullOrWhiteSpace(lyricInfo.YrTrLyrics))
        {
            ConvertTranslation(lyricInfo.TrLyrics, lyrics);
            return;
        }

        var targetLyrics = ParseLrc(lyricInfo.YrTrLyrics);
        if (migrateLyrics && !string.IsNullOrWhiteSpace(lyricInfo.TrLyrics))
            targetLyrics = MigrateLyrics(targetLyrics, ParseLrc(lyricInfo.TrLyrics));

        ApplyAuxiliaryLyrics(targetLyrics, lyrics, static (lyric, text) => lyric.Translation = text);
    }

    public static void ConvertNeteaseRomaji(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyricAllText) || lyrics.Count == 0) return;

        ApplyAuxiliaryLyrics(ParseLrc(lyricAllText), lyrics, static (lyric, text) => lyric.Romaji = text);
    }

    public static void ConvertYrcNeteaseRomaji(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics,
        bool migrateLyrics = false)
    {
        if (lyrics.Count == 0) return;
        if (string.IsNullOrWhiteSpace(lyricInfo.YrNeteaseRomaji))
        {
            ConvertNeteaseRomaji(lyricInfo.NeteaseRomaji, lyrics);
            return;
        }

        var targetLyrics = ParseLrc(lyricInfo.YrNeteaseRomaji);
        if (migrateLyrics && !string.IsNullOrWhiteSpace(lyricInfo.NeteaseRomaji))
            targetLyrics = MigrateLyrics(targetLyrics, ParseLrc(lyricInfo.NeteaseRomaji));

        ApplyAuxiliaryLyrics(targetLyrics, lyrics, static (lyric, text) => lyric.Romaji = text);
    }

    public static async Task ConvertKawazuRomaji(List<SongLyric> lyrics, KawazuConverter? kawazu)
    {
        if (kawazu is null) return;
        foreach (var lyricItem in lyrics)
            if (!string.IsNullOrWhiteSpace(lyricItem.Text))
            {
                if (!Utilities.HasKana(lyricItem.Text)) continue;
                lyricItem.Romaji =
                    await kawazu.Convert(lyricItem.Text, To.Romaji, Mode.Separated);
                if (lyricItem.Syllables is not { Count: > 0 } syllables) continue;
                var list = await kawazu.GetDivisions(lyricItem.Text, To.Romaji,
                    Mode.Separated, RomajiSystem.Hepburn, "", "");
                SetRomajiKaraoke(list, syllables);
            }
    }

    public static void SetRomajiKaraoke(List<Division> romajiInfo, List<LyricSyllable> syllables)
    {
        var elements = new List<RomajiElementCursor>();
        foreach (var division in romajiInfo)
            elements.AddRange(division.Select(element => new RomajiElementCursor(
                element.Element ?? string.Empty,
                element.HiraNotation ?? string.Empty)));

        var elementIndex = 0;
        foreach (var syllable in syllables)
        {
            var currentWord = syllable.Text.Trim();
            if (string.IsNullOrEmpty(currentWord) || !currentWord.Any(IsLyricCharacter))
            {
                syllable.Transliteration = null;
                continue;
            }

            SkipEmptyElements(elements, ref elementIndex);
            if (elementIndex >= elements.Count)
            {
                syllable.Transliteration = null;
                continue;
            }

            syllable.Transliteration = TryConsumeWord(elements, ref elementIndex, currentWord);
        }
    }

    private static string? TryConsumeWord(List<RomajiElementCursor> elements, ref int elementIndex, string currentWord)
    {
        var hiraBuilder = new StringBuilder();
        var remainingWord = currentWord;

        while (!string.IsNullOrEmpty(remainingWord))
        {
            SkipEmptyElements(elements, ref elementIndex);
            if (elementIndex >= elements.Count) break;

            var element = elements[elementIndex];
            var remainingElement = element.RemainingElement;
            if (string.IsNullOrEmpty(remainingElement))
            {
                elementIndex++;
                continue;
            }

            if (remainingWord.StartsWith(remainingElement, StringComparison.Ordinal))
            {
                hiraBuilder.Append(element.ConsumeAll());
                remainingWord = remainingWord[remainingElement.Length..];
                elementIndex++;
                continue;
            }

            if (remainingElement.StartsWith(remainingWord, StringComparison.Ordinal))
            {
                hiraBuilder.Append(element.ConsumePrefix(remainingWord.Length));
                remainingWord = string.Empty;
                continue;
            }

            var elementPrefixLength = GetCommonPrefixLength(remainingWord, remainingElement);
            if (elementPrefixLength > 0)
            {
                hiraBuilder.Append(element.ConsumePrefix(elementPrefixLength));
                remainingWord = remainingWord[elementPrefixLength..];
                continue;
            }

            var containedIndex = remainingElement.IndexOf(remainingWord, StringComparison.Ordinal);
            if (containedIndex >= 0)
            {
                element.ConsumeElementPrefix(containedIndex);
                hiraBuilder.Append(element.ConsumePrefix(remainingWord.Length));
                remainingWord = string.Empty;
                continue;
            }

            if (!TrySeekNextMatchingElement(elements, ref elementIndex, remainingWord)) break;
        }

        var hira = hiraBuilder.ToString();
        return string.IsNullOrWhiteSpace(hira)
            ? null
            : Utilities.ToRawRomaji(hira, RomajiSystem.Hepburn, true);
    }

    private static bool TrySeekNextMatchingElement(
        List<RomajiElementCursor> elements,
        ref int elementIndex,
        string currentWord)
    {
        var searchLimit = Math.Min(elements.Count, elementIndex + 4);
        for (var i = elementIndex + 1; i < searchLimit; i++)
        {
            var remainingElement = elements[i].RemainingElement;
            if (string.IsNullOrEmpty(remainingElement)) continue;
            if (currentWord.StartsWith(remainingElement, StringComparison.Ordinal) ||
                remainingElement.StartsWith(currentWord, StringComparison.Ordinal) ||
                remainingElement.Contains(currentWord, StringComparison.Ordinal) ||
                GetCommonPrefixLength(currentWord, remainingElement) > 0)
            {
                elementIndex = i;
                return true;
            }
        }

        return false;
    }

    private static void SkipEmptyElements(List<RomajiElementCursor> elements, ref int elementIndex)
    {
        while (elementIndex < elements.Count && elements[elementIndex].IsConsumed) elementIndex++;
    }

    private static int GetCommonPrefixLength(string first, string second)
    {
        var max = Math.Min(first.Length, second.Length);
        var length = 0;
        while (length < max && first[length] == second[length]) length++;

        return length;
    }

    private static int GetHiraLengthForElementPrefix(string element, string hiraNotation, int elementPrefixLength)
    {
        if (string.IsNullOrEmpty(hiraNotation)) return 0;
        if (elementPrefixLength >= element.Length) return hiraNotation.Length;
        if (elementPrefixLength <= 0) return 0;

        var prefix = element[..elementPrefixLength];
        if (prefix.All(IsKana)) return Math.Min(prefix.Length, hiraNotation.Length);

        if (element.All(IsKana) && element.Length > 0) return Math.Clamp(elementPrefixLength, 0, hiraNotation.Length);

        var suffix = element[elementPrefixLength..];
        if (suffix.All(IsKana) &&
            ToHiragana(hiraNotation).EndsWith(ToHiragana(suffix), StringComparison.Ordinal))
            return Math.Max(0, hiraNotation.Length - suffix.Length);

        return hiraNotation.Length;
    }

    private static bool IsLyricCharacter(char character)
    {
        return !char.IsWhiteSpace(character) && !char.IsPunctuation(character) && !char.IsSymbol(character);
    }

    private static bool IsKana(char character)
    {
        return character is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9f';
    }

    private static string ToHiragana(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character is >= '\u30a1' and <= '\u30f6'
                ? (char)(character - 0x60)
                : character);

        return builder.ToString();
    }

    public static async Task ConvertRomaji(
        PureLyricInfo pureLyricInfo,
        List<SongLyric> lyrics,
        LyricConversionOptions options)
    {
        switch (options.RomajiSource)
        {
            case RomajiSource.None:
                break;
            case RomajiSource.AutoSelect:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo)
                        ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics, options.MigrateLyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                else
                    await ConvertKawazuRomaji(lyrics, options.KawazuConverter);
                break;
            case RomajiSource.NeteaseOnly:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo)
                        ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics, options.MigrateLyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                break;
            case RomajiSource.KawazuOnly:
                await ConvertKawazuRomaji(lyrics, options.KawazuConverter);
                break;
        }
    }

    public static List<SongLyric> ConvertKaraok(PureLyricInfo pureLyricInfo, bool migrateLyrics = false)
    {
        if (pureLyricInfo is not KaraokLyricInfo karaokeLyricInfo ||
            string.IsNullOrWhiteSpace(karaokeLyricInfo.KaraokLyric))
            return ConvertPureLyric(pureLyricInfo?.PureLyrics);

        try
        {
            var parsedLyrics = ParseYrc(karaokeLyricInfo.KaraokLyric);
            if (migrateLyrics && !string.IsNullOrWhiteSpace(pureLyricInfo.PureLyrics))
                parsedLyrics = MigrateLyrics(parsedLyrics, ParseLrc(pureLyricInfo.PureLyrics));

            if (parsedLyrics.Count != 0) return parsedLyrics;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Karaoke lyric conversion failed: {ex.Message}");
        }

        return ConvertPureLyric(pureLyricInfo.PureLyrics);
    }

    public static ALRCFile ConvertToALRC(List<SongLyric> lyric, double durationMs = 0)
    {
        var lines = new List<ALRCLine>();
        var alrc = new ALRCFile
        {
            Schema = "https://github.com/kengwang/ALRC/blob/main/schemas/v1.json",
            LyricInfo = null,
            SongInfo = null,
            Header = null,
            Lines = lines
        };
        var lastLine = new ALRCLine();
        foreach (var songLyric in lyric)
        {
            var line = new ALRCLine
            {
                Start = (long)songLyric.StartTime.TotalMilliseconds,
                LineStyle = null,
                RawText = songLyric.Text,
                Transliteration = songLyric.Romaji?.Trim(),
                Translation = songLyric.Translation?.Trim()
            };
            lastLine.End = line.Start;
            lastLine = line;
            if (songLyric.Syllables is { Count: > 0 } syllables)
                line.Words =
                [
                    .. syllables.Select(s => new ALRCWord
                    {
                        Start = (long)s.StartTime.TotalMilliseconds,
                        End = (long)(s.StartTime + s.Duration).TotalMilliseconds,
                        Word = s.Text,
                        Transliteration = string.IsNullOrWhiteSpace(s.Transliteration) ? null : s.Transliteration
                    })
                ];
            lines.Add(line);
        }

        if (lines.LastOrDefault() is { End: null or <= 0 } last) last.End = (long)durationMs;

        return alrc;
    }

    private static List<SongLyric> ParseLrc(string input)
    {
        return
        [
            .. LyricifyLrcParser.ParseLyrics(input.AsSpan())
                .Where(static line => line.StartTime.HasValue)
                .Select(ToSongLyric)
        ];
    }

    private static List<SongLyric> ParseYrc(string input)
    {
        return
        [
            .. LyricifyYrcParser.ParseOnlyLyrics(input.AsSpan())
                .Where(static line => line.StartTime.HasValue)
                .Select(ToSongLyric)
                .OrderBy(static line => line.StartTime)
        ];
    }

    private static SongLyric ToSongLyric(ILineInfo line)
    {
        var startMilliseconds = line.StartTime.GetValueOrDefault();
        var result = new SongLyric
        {
            Text = line.Text,
            StartTime = TimeSpan.FromMilliseconds(startMilliseconds),
            Duration = TimeSpan.FromMilliseconds(Math.Max(0, line.EndTime.GetValueOrDefault(startMilliseconds) -
                                                              startMilliseconds))
        };

        if (line is SyllableLineInfo { Syllables.Count: > 0 } syllableLine)
            result.Syllables =
            [
                .. syllableLine.Syllables.Select(static syllable => new LyricSyllable
                {
                    Text = syllable.Text,
                    StartTime = TimeSpan.FromMilliseconds(syllable.StartTime),
                    Duration = TimeSpan.FromMilliseconds(Math.Max(0, syllable.EndTime - syllable.StartTime))
                })
            ];

        return result;
    }

    private static void ApplyAuxiliaryLyrics(
        IEnumerable<SongLyric> auxiliaryLyrics,
        List<SongLyric> lyrics,
        Action<SongLyric, string> apply)
    {
        var lyricsByTime = new Dictionary<TimeSpan, SongLyric>(lyrics.Count * 2);
        foreach (var lyric in lyrics)
        {
            lyricsByTime.TryAdd(lyric.StartTime, lyric);
            if (lyric.MatchedStartTime is { } matchedStartTime)
                lyricsByTime.TryAdd(matchedStartTime, lyric);
        }

        foreach (var auxiliaryLyric in auxiliaryLyrics)
            if (lyricsByTime.TryGetValue(auxiliaryLyric.StartTime, out var lyric))
                apply(lyric, auxiliaryLyric.Text);
    }

    private static List<SongLyric> MigrateLyrics(
        IReadOnlyList<SongLyric> target,
        IReadOnlyList<SongLyric> source,
        double similarity = 80,
        double rangeMilliseconds = 750)
    {
        var result = new List<SongLyric>(source.Count);
        var normalizedTargets = target
            .Select(static line => (Line: line, Text: NormalizeLyricText(line.Text)))
            .ToList();
        foreach (var sourceLine in source)
        {
            if (string.IsNullOrWhiteSpace(sourceLine.Text)) continue;

            SongLyric? bestMatch = null;
            var bestSimilarity = double.MinValue;
            var normalizedSource = NormalizeLyricText(sourceLine.Text);
            foreach (var targetLine in normalizedTargets)
            {
                if (targetLine.Line.StartTime == sourceLine.StartTime)
                {
                    bestMatch = targetLine.Line;
                    break;
                }

                if (Math.Abs((targetLine.Line.StartTime - sourceLine.StartTime).TotalMilliseconds) >
                    rangeMilliseconds)
                    continue;

                var currentSimilarity = StringHelper.ComputeTextSame(normalizedSource, targetLine.Text);
                if (currentSimilarity > bestSimilarity)
                {
                    bestMatch = targetLine.Line;
                    bestSimilarity = currentSimilarity;
                }
            }

            if (bestMatch is not null && (bestMatch.StartTime == sourceLine.StartTime || bestSimilarity > similarity))
            {
                if (bestMatch.StartTime != sourceLine.StartTime)
                    bestMatch.MatchedStartTime = sourceLine.StartTime;
                result.Add(bestMatch);
            }
            else
            {
                result.Add(sourceLine);
            }
        }

        result.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));
        return result;
    }

    private static string NormalizeLyricText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
            if (!char.IsPunctuation(character) && !char.IsWhiteSpace(character))
                builder.Append(character);

        return builder.ToString();
    }

    public readonly record struct LyricConversionOptions(
        bool MigrateLyrics,
        RomajiSource RomajiSource,
        KawazuConverter? KawazuConverter);

    private sealed class RomajiElementCursor(string element, string hiraNotation)
    {
        private readonly string _element = element;
        private readonly string _hiraNotation = hiraNotation;

        public int ElementOffset { get; private set; }
        public int HiraOffset { get; private set; }
        public string RemainingElement => ElementOffset >= _element.Length ? string.Empty : _element[ElementOffset..];
        public string RemainingHira => HiraOffset >= _hiraNotation.Length ? string.Empty : _hiraNotation[HiraOffset..];
        public bool IsConsumed => string.IsNullOrWhiteSpace(RemainingElement);

        public string ConsumeAll()
        {
            var hira = RemainingHira;
            ElementOffset = _element.Length;
            HiraOffset = _hiraNotation.Length;
            return hira;
        }

        public string ConsumePrefix(int elementLength)
        {
            elementLength = Math.Clamp(elementLength, 0, RemainingElement.Length);
            if (elementLength == 0) return string.Empty;

            var hiraLength = GetHiraLengthForElementPrefix(RemainingElement, RemainingHira, elementLength);
            var hira = RemainingHira[..hiraLength];
            ElementOffset += elementLength;
            HiraOffset += hiraLength;
            return hira;
        }

        public void ConsumeElementPrefix(int elementLength)
        {
            elementLength = Math.Clamp(elementLength, 0, RemainingElement.Length);
            ElementOffset += elementLength;
        }
    }
}
