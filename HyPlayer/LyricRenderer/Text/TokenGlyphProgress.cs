#nullable enable

using System;

namespace HyPlayer.LyricRenderer.Text;

internal static class TokenGlyphProgress
{
    private const long WholeTokenLiftDurationThreshold = 1000;

    public static float GetLiftProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
    {
        if (frame.CurrentTokenIndex < 0)
        {
            if (cluster.LayerClusterCount <= 0) return Math.Clamp(frame.CurrentTokenProgress, 0, 1);
            return Math.Clamp(frame.LineProgress * cluster.LayerClusterCount - cluster.LayerClusterIndex, 0, 1);
        }

        if (cluster.TokenStartIndex >= 0 && cluster.TokenEndIndexExclusive > cluster.TokenStartIndex)
        {
            if (cluster.TokenEndIndexExclusive <= frame.CurrentTokenIndex) return 1;
            if (cluster.TokenStartIndex > frame.CurrentTokenIndex) return 0;
            if (frame.CurrentTokenDuration < WholeTokenLiftDurationThreshold)
                return Math.Clamp(frame.CurrentTokenProgress, 0, 1);

            return GetSourceRangeProgress(cluster, frame);
        }

        return GetSourceRangeProgress(cluster, frame);
    }

    public static float GetHighlightProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
    {
        if (frame.CurrentTokenIndex < 0)
        {
            if (cluster.LayerClusterCount <= 0) return Math.Clamp(frame.CurrentTokenProgress, 0, 1);
            return Math.Clamp(frame.LineProgress * cluster.LayerClusterCount - cluster.LayerClusterIndex, 0, 1);
        }

        if (cluster.TokenStartIndex >= 0 && cluster.TokenEndIndexExclusive > cluster.TokenStartIndex)
        {
            if (cluster.TokenEndIndexExclusive <= frame.CurrentTokenIndex) return 1;
            if (cluster.TokenStartIndex > frame.CurrentTokenIndex) return 0;
        }

        return GetSourceRangeProgress(cluster, frame);
    }

    public static float GetSourceRangeProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
    {
        if (cluster.SourceStart < 0 || cluster.SourceEnd <= cluster.SourceStart)
            return Math.Clamp(frame.CurrentTokenProgress, 0, 1);

        var sourcePosition = cluster.Layer == LyricTextLayer.Transliteration
            ? frame.CurrentTransliterationSourcePosition
            : frame.CurrentLyricSourcePosition;
        return Math.Clamp((sourcePosition - cluster.SourceStart) / (cluster.SourceEnd - cluster.SourceStart), 0, 1);
    }
}