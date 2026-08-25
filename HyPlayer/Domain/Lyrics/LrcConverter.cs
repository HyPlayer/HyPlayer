using ALRC.Abstraction;
using ALRC.Converters;
using HyPlayer.Domain.Lyrics.LyricEnhancers;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Text;
using System;
using System.Linq;
using System.Collections.Generic;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace HyPlayer.Domain.Lyrics;

public static class LrcConverter
{

    public static readonly List<ILyricEnhancer<bool>> LyricEnhancers =
    [
        new BreathLineEnhancer(),
        new NearbyLineAlignmentEnhancer(),
        new SublineAlignmentEnhancer()
    ];

    public static List<RenderingLyricLine> Convert(
        ALRCFile alrc,
        List<LyricInfoMetadata>? lyricMetadata = null,
        List<LyricInfoMetadata>? songMetadata = null,
        bool optimizeLyric = false)
    {
        ArgumentNullException.ThrowIfNull(alrc);
        if (optimizeLyric)
        {
            foreach (var lyricEnhancer in LyricEnhancers)
                alrc = lyricEnhancer.Enhance(true, alrc);
        }

        var styles = BuildStyleTable(alrc);
        var resolved = ResolveLines(alrc);
        var grouped = GroupLines(resolved);
        var result = new List<RenderingLyricLine>(grouped.Count + (lyricMetadata?.Count ?? 0));

        foreach (var item in grouped)
            result.Add(CreateRenderingLine(item, styles));

        if (lyricMetadata is { Count: > 0 })
        {
            var nextGroupIndex = grouped.Select(item => item.GroupIndex).DefaultIfEmpty(-1).Max() + 1;
            foreach (var metadata in lyricMetadata)
            {
                result.Add(new ActionLyricLine
                {
                    Text = $"{metadata.DisplayName}: {metadata.Value}",
                    ActionUri = metadata.ActionUri,
                    FactoIndex = result.Count,
                    GroupIndex = nextGroupIndex++
                });
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, ALRCStyle> BuildStyleTable(ALRCFile alrc)
    {
        var result = new Dictionary<string, ALRCStyle>(StringComparer.Ordinal);
        foreach (var style in alrc.Header?.Styles ?? [])
        {
            if (!string.IsNullOrEmpty(style.Id)) result[style.Id] = style;
        }
        return result;
    }

    private static List<ResolvedLine> ResolveLines(ALRCFile alrc)
    {
        var result = new List<ResolvedLine>(alrc.Lines.Count);
        for (var index = 0; index < alrc.Lines.Count; index++)
        {
            var line = alrc.Lines[index];
            var words = line.Words is { Count: > 0 } ? line.Words : null;
            var start = line.Start ?? words?.First().Start ?? 0;
            var nextStart = index + 1 < alrc.Lines.Count
                ? alrc.Lines[index + 1].Start ?? alrc.Lines[index + 1].Words?.FirstOrDefault()?.Start
                : null;
            var end = line.End ?? words?.Last().End ?? nextStart ?? alrc.LyricInfo?.Duration ?? start;
            result.Add(new ResolvedLine(line, index, start, end));
        }
        return result;
    }

    private static List<ResolvedLine> GroupLines(IReadOnlyList<ResolvedLine> lines)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            if (!string.IsNullOrEmpty(lines[index].Line.Id)) ids[lines[index].Line.Id!] = index;
        }

        var roots = new int[lines.Count];
        for (var index = 0; index < lines.Count; index++)
            roots[index] = ResolveRoot(index, lines, ids);

        var groups = Enumerable.Range(0, lines.Count)
            .GroupBy(index => roots[index])
            .OrderBy(group => group.Key)
            .ToList();
        var result = new List<ResolvedLine>(lines.Count);
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var members = group.OrderBy(index => index == group.Key ? int.MinValue : index).ToList();
            var groupStart = members.Min(index => lines[index].Start);
            var groupEnd = members.Max(index => lines[index].End);
            foreach (var member in members)
            {
                result.Add(lines[member] with
                {
                    GroupIndex = groupIndex,
                    GroupStart = groupStart,
                    GroupEnd = groupEnd
                });
            }
        }
        return result;
    }

    private static int ResolveRoot(
        int start,
        IReadOnlyList<ResolvedLine> lines,
        IReadOnlyDictionary<string, int> ids)
    {
        var visited = new HashSet<int>();
        var current = start;
        while (true)
        {
            if (!visited.Add(current)) return start;
            var parentId = lines[current].Line.ParentLineId;
            if (string.IsNullOrEmpty(parentId)) return current;
            if (!ids.TryGetValue(parentId, out var parent)) return start;
            current = parent;
        }
    }

    private static RenderingLyricLine CreateRenderingLine(
        ResolvedLine item,
        IReadOnlyDictionary<string, ALRCStyle> styles)
    {
        var source = item.Line;
        styles.TryGetValue(source.LineStyle ?? string.Empty, out var style);
        var words = source.Words is { Count: > 0 } ? source.Words : null;
        var text = words is null ? source.RawText ?? string.Empty : string.Concat(words.Select(word => word.Word));
        var transliteration = words?.Any(word => !string.IsNullOrEmpty(word.Transliteration)) == true
            ? string.Concat(words.Select(word => word.Transliteration ?? string.Empty))
            : source.Transliteration;
        var typography = style is null
            ? null
            : new RenderTypography
            {
                Alignment = style.Position switch
                {
                    ALRCStylePosition.Left => TextAlignment.Left,
                    ALRCStylePosition.Center => TextAlignment.Center,
                    ALRCStylePosition.Right => TextAlignment.Right,
                    _ => null
                }
            };

        RenderingLyricLine line;
        if (string.IsNullOrWhiteSpace(text) && item.End - item.Start >= 1500)
        {
            line = new ProgressBarRenderingLyricLine { Typography = typography };
        }
        else
        {
            line = new TextRenderingLyricLine
            {
                Text = text,
                Transliteration = transliteration,
                Translation = source.Translation,
                Tokens = words?.Select(word => new LyricTextToken(
                    word.Word,
                    word.Start,
                    word.End,
                    word.Transliteration)).ToList() ?? [],
                Typography = typography
            };
        }

        line.SourceLine = source;
        line.SourceStyle = style;
        line.StyleTable = styles;
        line.HiddenOnBlur = style?.HiddenOnBlur == true;
        line.FactoIndex = item.OriginalIndex;
        line.GroupIndex = item.GroupIndex;
        line.GroupStartTime = item.GroupStart;
        line.GroupEndTime = item.GroupEnd;
        line.StartTime = item.Start;
        line.EndTime = item.End;
        line.KeyFrames = [item.Start, item.End];
        return line;
    }


    private sealed record ResolvedLine(ALRCLine Line, int OriginalIndex, long Start, long End)
    {
        public int GroupIndex { get; init; }
        public long GroupStart { get; init; }
        public long GroupEnd { get; init; }
    }
}
