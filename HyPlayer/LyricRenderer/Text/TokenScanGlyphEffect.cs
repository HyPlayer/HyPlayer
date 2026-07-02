#nullable enable

using System;

namespace HyPlayer.LyricRenderer.Text;

public sealed class TokenScanGlyphEffect : ILyricGlyphEffect
{
    private const float LiftAmount = 3;
    private const long WholeTokenLiftDurationThreshold = 1000;

    public void Apply(LyricGlyphEffectContext context, ref LyricGlyphDrawState state)
    {
        var cluster = context.Cluster;
        var frame = context.Frame;

        if (cluster.Layer == LyricTextLayer.Transliteration && !context.RenderContext.Effects.TransliterationScanning)
        {
            state.Opacity = 1;
            return;
        }

        var progress = GetClusterProgress(cluster, frame);
        state.Origin.Y -= LiftAmount * progress;
        state.Opacity = Math.Clamp(0.3f + 0.7f * progress, 0.3f, 1f);
    }

    private static float GetClusterProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
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
            {
                return Math.Clamp(frame.CurrentTokenProgress, 0, 1);
            }

            return GetSourceRangeProgress(cluster, frame);
        }

        return GetSourceRangeProgress(cluster, frame);
    }

    private static float GetSourceRangeProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
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
}
