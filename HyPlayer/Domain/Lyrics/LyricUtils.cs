using ALRC.Abstraction;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Lyrics.LyricParser.Implementation;
using HyPlayer.Domain.Settings;
using Kawazu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HyPlayer.Domain.Lyrics;

public static class Utils
{
    public readonly record struct LyricConversionOptions(
        bool MigrateLyrics,
        RomajiSource RomajiSource,
        KawazuConverter? KawazuConverter);

    public static List<SongLyric> ConvertPureLyric(string lyricAllText)
    {
        if (string.IsNullOrWhiteSpace(lyricAllText))
            return [];

        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        return [.. parsedlyrics.Lines.OrderBy(t => t.StartTime).Select(lyricsLine => new SongLyric
        { LyricLine = lyricsLine, Translation = null })];
    }

    public static void ConvertTranslation(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyricAllText) || lyrics.Count == 0)
            return;

        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        foreach (var lyricsLine in parsedlyrics.Lines)
            foreach (var songLyric in lyrics.Where(songLyric =>
                         songLyric.LyricLine.StartTime == lyricsLine.StartTime))
            {
                songLyric.Translation = lyricsLine.CurrentLyric;
                break;
            }
    }

    public static void ConvertYrcTranslation(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics, bool migrateLyrics = false)
    {
        if (lyrics.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(lyricInfo.YrTrLyrics))
        {
            ConvertTranslation(lyricInfo.TrLyrics, lyrics);
            return;
        }

        var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrTrLyrics.AsSpan());
        if (migrateLyrics && !string.IsNullOrWhiteSpace(lyricInfo.TrLyrics))
        {
            var sourceLyrics = LrcParser.ParseLrc(lyricInfo.TrLyrics.AsSpan());
            var migrated = MigrationTool.Migrate(targetLyrics, sourceLyrics);
            foreach (var lyricsLine in migrated.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
                {
                    lyric.Translation = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
        else
        {
            foreach (var lyricsLine in targetLyrics.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
                {
                    lyric.Translation = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
    }

    public static void ConvertNeteaseRomaji(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrEmpty(lyricAllText)) return;
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        foreach (var lyricsLine in parsedlyrics.Lines)
            foreach (var songLyric in lyrics.Where(songLyric =>
                         songLyric.LyricLine.StartTime == lyricsLine.StartTime ||
                         songLyric.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
            {
                songLyric.Romaji = lyricsLine.CurrentLyric;
                break;
            }
    }

    public static void ConvertYrcNeteaseRomaji(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics, bool migrateLyrics = false)
    {
        if (lyrics.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(lyricInfo.YrNeteaseRomaji))
        {
            ConvertNeteaseRomaji(lyricInfo.NeteaseRomaji, lyrics);
            return;
        }

        var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrNeteaseRomaji.AsSpan());
        if (migrateLyrics && !string.IsNullOrWhiteSpace(lyricInfo.NeteaseRomaji))
        {
            var sourceLyrics = LrcParser.ParseLrc(lyricInfo.NeteaseRomaji.AsSpan());
            var migrated = MigrationTool.Migrate(targetLyrics, sourceLyrics);
            foreach (var lyricsLine in migrated.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
                {
                    lyric.Romaji = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
        else
        {
            foreach (var lyricsLine in targetLyrics.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
                {
                    lyric.Romaji = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
    }

    public static async Task ConvertKawazuRomaji(List<SongLyric> lyrics, KawazuConverter? kawazu)
    {
        if (kawazu is null) return;
        foreach (var lyricItem in lyrics)
        {
            if (!string.IsNullOrWhiteSpace(lyricItem.LyricLine.CurrentLyric))
            {
                if (!Utilities.HasKana(lyricItem.LyricLine.CurrentLyric)) continue;
                lyricItem.Romaji =
                    await kawazu.Convert(lyricItem.LyricLine.CurrentLyric, To.Romaji, Mode.Separated);
                if (lyricItem.LyricLine is not KaraokeLyricsLine klyric) continue;
                var list = await kawazu.GetDivisions(lyricItem.LyricLine.CurrentLyric, To.Romaji,
                    Mode.Separated, RomajiSystem.Hepburn, "", "");
                SetRomajiKaraoke(list, [.. klyric.WordInfos]);
            }
        }
    }

    public static void SetRomajiKaraoke(List<Division> romajiInfo, List<KaraokeWordInfo> wordInfo)
    {
        var elements = new List<RomajiElementCursor>();
        foreach (var division in romajiInfo)
        {
            elements.AddRange(division.Select(element => new RomajiElementCursor(
                element.Element ?? string.Empty,
                element.HiraNotation ?? string.Empty)));
        }

        var elementIndex = 0;
        foreach (var word in wordInfo)
        {
            var currentWord = word.CurrentWords?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(currentWord) || !currentWord.Any(IsLyricCharacter))
            {
                word.Transliteration = null;
                continue;
            }

            SkipEmptyElements(elements, ref elementIndex);
            if (elementIndex >= elements.Count)
            {
                word.Transliteration = null;
                continue;
            }

            word.Transliteration = TryConsumeWord(elements, ref elementIndex, currentWord);
        }
    }

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

            if (!TrySeekNextMatchingElement(elements, ref elementIndex, remainingWord))
            {
                break;
            }
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
        while (elementIndex < elements.Count && elements[elementIndex].IsConsumed)
        {
            elementIndex++;
        }
    }

    private static int GetCommonPrefixLength(string first, string second)
    {
        var max = Math.Min(first.Length, second.Length);
        var length = 0;
        while (length < max && first[length] == second[length])
        {
            length++;
        }

        return length;
    }

    private static int GetHiraLengthForElementPrefix(string element, string hiraNotation, int elementPrefixLength)
    {
        if (string.IsNullOrEmpty(hiraNotation)) return 0;
        if (elementPrefixLength >= element.Length) return hiraNotation.Length;
        if (elementPrefixLength <= 0) return 0;

        var prefix = element[..elementPrefixLength];
        if (prefix.All(IsKana))
        {
            return Math.Min(prefix.Length, hiraNotation.Length);
        }

        if (element.All(IsKana) && element.Length > 0)
        {
            return Math.Clamp(elementPrefixLength, 0, hiraNotation.Length);
        }

        var suffix = element[elementPrefixLength..];
        if (suffix.All(IsKana) &&
            ToHiragana(hiraNotation).EndsWith(ToHiragana(suffix), StringComparison.Ordinal))
        {
            return Math.Max(0, hiraNotation.Length - suffix.Length);
        }

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
        {
            builder.Append(character is >= '\u30a1' and <= '\u30f6'
                ? (char)(character - 0x60)
                : character);
        }

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
        if (pureLyricInfo is not KaraokLyricInfo karaokLyricInfo ||
            string.IsNullOrWhiteSpace(karaokLyricInfo.KaraokLyric))
            return ConvertPureLyric(pureLyricInfo?.PureLyrics);

        try
        {
            var parsedLyrics = KaraokeParser.ParseKaraoke(karaokLyricInfo.KaraokLyric.AsSpan());
            if (migrateLyrics && !string.IsNullOrWhiteSpace(pureLyricInfo.PureLyrics))
            {
                var pureLyrics = LrcParser.ParseLrc(pureLyricInfo.PureLyrics.AsSpan());
                var migrated = MigrationTool.Migrate(parsedLyrics, pureLyrics);
                if (migrated.Lines.Count != 0)
                    return [.. migrated.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t })];
            }

            if (parsedLyrics.Lines.Count != 0)
                return [.. parsedLyrics.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t })];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Karaoke lyric conversion failed: {ex.Message}");
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
                Start = (long)songLyric.LyricLine.StartTime.TotalMilliseconds,
                LineStyle = null,
                RawText = songLyric.LyricLine.CurrentLyric,
                Transliteration = songLyric.Romaji?.Trim(),
                Translation = songLyric.Translation?.Trim()
            };
            lastLine.End = line.Start;
            lastLine = line;
            if (songLyric.LyricLine is KaraokeLyricsLine lrcLyricsLine)
            {
                line.Words = [.. lrcLyricsLine.WordInfos.Select(s => new ALRCWord
                {
                    Start = (long)s.StartTime.TotalMilliseconds,
                    End = (long)(s.StartTime + s.Duration).TotalMilliseconds,
                    Word = s.CurrentWords,
                    Transliteration = string.IsNullOrWhiteSpace(s.Transliteration) ? null : s.Transliteration
                })];
            }
            lines.Add(line);
        }

        if (lines.LastOrDefault() is { End: null or <= 0 } last) last.End = (long)durationMs;

        return alrc;
    }
}
