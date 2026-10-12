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
    }

    private DirectLyricVisualLine[] PrepareLines(IReadOnlyList<LyricGlyphCluster> clusters, long start, long end)
    {
        var result = new List<DirectLyricVisualLine>();
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
                tokenToSpan.Add(pair.Key, spans.Count);
                spans.Add(new(token.StartTime, token.EndTime, b.Rtl ? b.Right : b.Left, b.Rtl ? b.Left : b.Right));
            }
            if (spans.Count == 0) spans.Add(new(start, end, rtl ? right : left, rtl ? left : right));
            var indexes = new int[last - first];
            for (var index = first; index < last; index++)
                indexes[index - first] = tokenToSpan.GetValueOrDefault(clusters[index].TokenStartIndex, 0);
            result.Add(new(first, last, FocusedLineRevealTimeline.Create(spans, left, right, rtl), indexes));
            first = last;
        }
        return result.ToArray();
    }
}
