#nullable enable

using System;

namespace HyPlayer.LyricRenderer.Text;

internal static class TokenGlyphProgress
{
    public static float GetLiftProgress(
        LyricGlyphCluster cluster,
        TextRenderFrame frame,
        long wholeTokenLiftDurationThreshold,
        float cooperativeLiftWindow,
        float cooperativeLiftEasingPower)
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
            if (frame.CurrentTokenDuration < wholeTokenLiftDurationThreshold)
            {
                return Math.Clamp(frame.CurrentTokenProgress, 0, 1);
            }

            if (cooperativeLiftWindow > 0.001f &&
                cluster.TokenClusterCount > 1 &&
                cluster.TokenStartIndex == frame.CurrentTokenIndex)
            {
                return GetCooperativeTokenProgress(cluster, frame, cooperativeLiftWindow, cooperativeLiftEasingPower);
            }

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
        {
            return Math.Clamp(frame.CurrentTokenProgress, 0, 1);
        }

        var sourcePosition = cluster.Layer == LyricTextLayer.Transliteration
            ? frame.CurrentTransliterationSourcePosition
            : frame.CurrentLyricSourcePosition;
        return Math.Clamp((sourcePosition - cluster.SourceStart) / (cluster.SourceEnd - cluster.SourceStart), 0, 1);
    }

    private static float GetCooperativeTokenProgress(
        LyricGlyphCluster cluster,
        TextRenderFrame frame,
        float window,
        float easingPower)
    {
        return GetCooperativeProgress(
            cluster.TokenClusterIndex,
            cluster.TokenClusterCount,
            frame.CurrentTokenProgress,
            window,
            easingPower);
    }

    private static float GetCooperativeProgress(
        int clusterIndex,
        int clusterCount,
        float currentProgress,
        float window,
        float easingPower)
    {
        var count = Math.Max(1, clusterCount);
        var wave = Math.Clamp(currentProgress, 0, 1) * (count - 1 + window);
        var progress = Math.Clamp((wave - clusterIndex) / window, 0, 1);
        if (progress <= 0 || progress >= 1)
        {
            return progress;
        }

        var smoothed = progress * progress * (3 - 2 * progress);
        return Math.Clamp((float)Math.Pow(smoothed, Math.Max(0.1f, easingPower)), 0, 1);
    }
}
