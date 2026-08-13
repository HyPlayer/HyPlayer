#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Windows.Data.Text;

namespace HyPlayer.LyricRenderer.Text;

internal static class InferredLyricWordTokenizer
{
    private static int _fallbackReported;

    public static IReadOnlyList<LyricTextToken> Infer(
        string text,
        string? transliteration,
        long lineStartTime,
        long lineEndTime)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var spans = GetWordSpans(text);
        if (spans.Count == 0)
        {
            if (Interlocked.Exchange(ref _fallbackReported, 1) == 0)
                System.Diagnostics.Debug.WriteLine(
                    "Windows WordsSegmenter 未能为歌词生成 Word；本次会话后续同类行将静默回退到 Unicode grapheme。");
            spans = GetGraphemeSpans(text);
        }
        var transliterationSlices = SplitProportionally(transliteration, spans.Count);
        var weights = spans.Select(span => Math.Max(1, CountGraphemes(text.AsSpan(span.Start, span.Length).ToString()))).ToArray();
        var totalWeight = Math.Max(1, weights.Sum());
        var duration = Math.Max(0, lineEndTime - lineStartTime);
        var elapsedWeight = 0;
        var result = new List<LyricTextToken>(spans.Count);
        for (var index = 0; index < spans.Count; index++)
        {
            var span = spans[index];
            var start = lineStartTime + duration * elapsedWeight / totalWeight;
            elapsedWeight += weights[index];
            var end = index == spans.Count - 1
                ? lineEndTime
                : lineStartTime + duration * elapsedWeight / totalWeight;
            result.Add(new LyricTextToken(
                text.Substring(span.Start, span.Length),
                start,
                end,
                transliterationSlices.Count > index ? transliterationSlices[index] : null,
                isInferred: true));
        }
        return result;
    }

    private static List<(int Start, int Length)> GetWordSpans(string text)
    {
        try
        {
            var segmenter = new WordsSegmenter(DetectLanguage(text));
            var segments = segmenter.GetTokens(text)
                .Select(item => item.SourceTextSegment)
                .Where(item => item.Length > 0)
                .OrderBy(item => item.StartPosition)
                .ToList();
            if (segments.Count == 0) return [];

            var spans = new List<(int Start, int Length)>(segments.Count);
            for (var index = 0; index < segments.Count; index++)
            {
                var start = index == 0 ? 0 : (int)segments[index].StartPosition;
                var end = index == segments.Count - 1
                    ? text.Length
                    : (int)segments[index + 1].StartPosition;
                if (end > start) spans.Add((start, end - start));
            }
            return spans;
        }
        catch
        {
            return [];
        }
    }

    private static List<(int Start, int Length)> GetGraphemeSpans(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var result = new List<(int Start, int Length)>(starts.Length);
        for (var index = 0; index < starts.Length; index++)
        {
            var end = index == starts.Length - 1 ? text.Length : starts[index + 1];
            result.Add((starts[index], end - starts[index]));
        }
        return result;
    }

    private static IReadOnlyList<string?> SplitProportionally(string? text, int count)
    {
        if (string.IsNullOrEmpty(text) || count <= 0) return [];
        var starts = StringInfo.ParseCombiningCharacters(text);
        var result = new string?[count];
        for (var index = 0; index < count; index++)
        {
            var glyphStart = starts.Length * index / count;
            var glyphEnd = starts.Length * (index + 1) / count;
            var sourceStart = glyphStart < starts.Length ? starts[glyphStart] : text.Length;
            var sourceEnd = glyphEnd < starts.Length ? starts[glyphEnd] : text.Length;
            result[index] = text[sourceStart..sourceEnd];
        }
        return result;
    }

    private static int CountGraphemes(string text) => StringInfo.ParseCombiningCharacters(text).Length;

    private static string DetectLanguage(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0x3040 and <= 0x30ff) return "ja-JP";
            if (value is >= 0xac00 and <= 0xd7af) return "ko-KR";
        }
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is >= 0x3400 and <= 0x9fff) return "zh-CN";
        }
        return "en-US";
    }
}
