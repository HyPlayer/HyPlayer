#nullable enable

using System;
using Windows.Foundation;

namespace HyPlayer.LyricRenderer.Text;

public sealed class RectRevealCalculator
{
    public float GetRevealProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
    {
        if (cluster.Layer == LyricTextLayer.Transliteration)
        {
            if (frame.CurrentTokenIndex < 0) return GetLineProgress(cluster, frame);

            return TokenGlyphProgress.GetSourceRangeProgress(cluster, frame);
        }

        if (frame.CurrentTokenIndex < 0) return GetLineProgress(cluster, frame);

        return TokenGlyphProgress.GetSourceRangeProgress(cluster, frame);
    }

    public Rect? GetRevealClip(LyricGlyphCluster cluster, LyricGlyphDrawState state, float revealProgress)
    {
        var progress = Math.Clamp(revealProgress, 0, 1);
        if (progress <= 0 || progress >= 1) return null;

        var width = cluster.VisualWidth * progress;
        if (width <= 0) return null;

        var offsetX = state.Origin.X - cluster.BaseState.Origin.X;
        var offsetY = state.Origin.Y - cluster.BaseState.Origin.Y;
        return new Rect(cluster.VisualLeft + offsetX, cluster.VisualTop + offsetY, width, cluster.VisualHeight);
    }

    private static float GetLineProgress(LyricGlyphCluster cluster, TextRenderFrame frame)
    {
        if (cluster.LayerClusterCount <= 0) return Math.Clamp(frame.LineProgress, 0, 1);

        if (cluster.VisualLineClusterCount <= 0) return Math.Clamp(frame.LineProgress, 0, 1);

        var lineCount = Math.Max(1, cluster.LayerVisualLineCount);
        var linePosition = Math.Clamp(frame.LineProgress, 0, 1) * lineCount;
        var currentLine = Math.Min(lineCount - 1, (int)Math.Floor(linePosition));
        if (cluster.VisualLineIndex < currentLine) return 1;

        if (cluster.VisualLineIndex > currentLine) return 0;

        var lineProgress = linePosition - currentLine;
        return Math.Clamp(lineProgress * cluster.VisualLineClusterCount - cluster.VisualLineClusterIndex, 0, 1);
    }
}