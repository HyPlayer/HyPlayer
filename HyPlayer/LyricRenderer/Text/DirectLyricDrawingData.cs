#nullable enable
using System;
using System.Collections.Generic;

namespace HyPlayer.LyricRenderer.Text;

/// <summary>A visual line prepared during layout rather than rediscovered on each frame.</summary>
internal sealed record DirectLyricVisualLine(int Start, int End, FocusedLineRevealTimeline Timeline, int[] SpanIndexes);

public sealed partial class LyricTextLayoutSnapshot
{
    internal DirectLyricVisualLine[] LyricVisualLines { get; private set; } = [];
    internal DirectLyricVisualLine[] TransliterationVisualLines { get; private set; } = [];
    internal long StartTimeMs { get; private set; }
    internal long EndTimeMs { get; private set; }
    internal LyricEmphasisPlan? Emphasis { get; private set; }

    internal void PrepareDirectDrawing(long start, long end)
    {
        StartTimeMs = start;
        EndTimeMs = end;
        LyricVisualLines = PrepareLines(LyricGlyphClusters, start, end);
        TransliterationVisualLines = PrepareLines(TransliterationGlyphClusters, start, end);
        Emphasis = HasRealWords ? LyricEmphasisPlan.Create(Tokens, LyricGlyphClusters) : null;
        LinkRuns(LyricGlyphClusters);
        LinkRuns(TransliterationGlyphClusters);
    }

    private static void LinkRuns(IReadOnlyList<LyricGlyphCluster> clusters)
    {
        for (var index = 1; index < clusters.Count; index++)
        {
            var previous = clusters[index - 1];
            var current = clusters[index];
            var from = previous.BaseState;
            var to = current.BaseState;
            current.ContinuesRun = current.VisualLineIndex == previous.VisualLineIndex &&
                                   ReferenceEquals(from.FontFace, to.FontFace) && from.FontSize == to.FontSize &&
                                   !from.IsSideways && !to.IsSideways && from.BidiLevel == to.BidiLevel &&
                                   (to.BidiLevel & 1) == 0 && from.MeasuringMode == to.MeasuringMode &&
                                   from.Origin.Y == to.Origin.Y &&
                                   Math.Abs(from.Origin.X + previous.AdvanceWidth - to.Origin.X) < 0.01f;
        }
    }

    private DirectLyricVisualLine[] PrepareLines(IReadOnlyList<LyricGlyphCluster> clusters, long start, long end)
    {
        var visualLines = new List<(int First, int Last, float Left, float Right, SortedDictionary<int, (float Left, float Right, bool Rtl)> Bounds)>();
        var tokenWidths = new Dictionary<int, float>();
        for (var first = 0; first < clusters.Count;)
        {
            var last = first + 1;
            while (last < clusters.Count && clusters[last].VisualLineIndex == clusters[first].VisualLineIndex) last++;
            var left = float.MaxValue;
            var right = float.MinValue;
            var bounds = new SortedDictionary<int, (float Left, float Right, bool Rtl)>();
            for (var index = first; index < last; index++)
            {
                var cluster = clusters[index];
                left = Math.Min(left, cluster.VisualLeft);
                right = Math.Max(right, cluster.VisualRight);
                var token = cluster.TokenStartIndex;
                if ((uint)token >= (uint)Tokens.Count) continue;
                if (bounds.TryGetValue(token, out var previous))
                    bounds[token] = (Math.Min(previous.Left, cluster.VisualLeft), Math.Max(previous.Right, cluster.VisualRight), previous.Rtl);
                else bounds[token] = (cluster.VisualLeft, cluster.VisualRight, (cluster.BaseState.BidiLevel & 1) != 0);
            }
            foreach (var pair in bounds)
                tokenWidths[pair.Key] = tokenWidths.GetValueOrDefault(pair.Key) + Math.Max(pair.Value.Right - pair.Value.Left, 0.001f);
            visualLines.Add((first, last, left, right, bounds));
            first = last;
        }

        var result = new DirectLyricVisualLine[visualLines.Count];
        var tokenOffsets = new Dictionary<int, float>();
        for (var lineIndex = 0; lineIndex < visualLines.Count; lineIndex++)
        {
            var (first, last, left, right, bounds) = visualLines[lineIndex];
            var rtl = (clusters[first].BaseState.BidiLevel & 1) != 0;
            if (bounds.Count > 1)
            {
                // Preserve the established visual-line direction selection when the
                // first shaped run has a different direction from the timed words.
                var firstCenter = 0f;
                var lastCenter = 0f;
                var firstBound = true;
                foreach (var pair in bounds)
                {
                    lastCenter = (pair.Value.Left + pair.Value.Right) / 2;
                    if (firstBound) { firstCenter = lastCenter; firstBound = false; }
                }
                rtl = lastCenter < firstCenter;
            }
            var spans = new List<FocusedLineRevealSpan>(Math.Max(1, bounds.Count));
            var tokenToSpan = new Dictionary<int, int>();
            foreach (var pair in bounds)
            {
                var token = Tokens[pair.Key];
                var b = pair.Value;
                var total = tokenWidths[pair.Key];
                var before = tokenOffsets.GetValueOrDefault(pair.Key);
                var after = before + Math.Max(b.Right - b.Left, 0.001f);
                tokenOffsets[pair.Key] = after;
                var duration = Math.Max(token.EndTime - token.StartTime, 0);
                var spanStart = token.StartTime + (long)Math.Round(duration * Math.Clamp(before / total, 0, 1));
                var spanEnd = token.StartTime + (long)Math.Round(duration * Math.Clamp(after / total, 0, 1));
                tokenToSpan.Add(pair.Key, spans.Count);
                spans.Add(new(spanStart, spanEnd, b.Rtl ? b.Right : b.Left, b.Rtl ? b.Left : b.Right));
            }
            if (spans.Count == 0) spans.Add(new(start, end, rtl ? right : left, rtl ? left : right));
            var indexes = new int[last - first];
            for (var index = first; index < last; index++)
                indexes[index - first] = tokenToSpan.GetValueOrDefault(clusters[index].TokenStartIndex, 0);
            result[lineIndex] = new(first, last, FocusedLineRevealTimeline.Create(spans, left, right, rtl), indexes);
        }
        return result;
    }
}
