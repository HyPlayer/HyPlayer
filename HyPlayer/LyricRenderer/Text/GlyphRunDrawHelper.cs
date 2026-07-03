#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public static class GlyphRunDrawHelper
{
    public static void DrawCluster(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state,
        Rect? clipRect = null)
    {
        if (state.SkipDraw)
        {
            return;
        }

        var alpha = (byte)Math.Round(state.Color.A * Math.Clamp(state.Opacity, 0, 1));
        if (alpha == 0)
        {
            return;
        }

        brush.Color = Color.FromArgb(alpha, state.Color.R, state.Color.G, state.Color.B);
        var originalTransform = session.Transform;

        try
        {
            if (Math.Abs(state.Scale - 1) > 0.001f)
            {
                session.Transform = Matrix3x2.CreateScale(state.Scale, state.Origin) * originalTransform;
            }

            if (clipRect is { } rect)
            {
                using var layer = session.CreateLayer(1, rect);
                DrawClusterContent(session, brush, state);
            }
            else
            {
                DrawClusterContent(session, brush, state);
            }
        }
        finally
        {
            session.Transform = originalTransform;
        }
    }

    private static void DrawClusterContent(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state)
    {
        if (state.BlurRadius > 0.001f)
        {
            DrawBlurApproximation(session, brush, state);
        }

        DrawGlyphRun(session, state, brush);
    }

    private static void DrawBlurApproximation(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state)
    {
        var originalColor = brush.Color;
        var blurAlpha = (byte)Math.Round(originalColor.A * 0.25f);
        if (blurAlpha == 0)
        {
            return;
        }

        brush.Color = Color.FromArgb(blurAlpha, originalColor.R, originalColor.G, originalColor.B);
        var offset = Math.Clamp(state.BlurRadius, 0.5f, 3f);
        var originalOrigin = state.Origin;
        state.Origin = originalOrigin + new Vector2(-offset, 0);
        DrawGlyphRun(session, state, brush);
        state.Origin = originalOrigin + new Vector2(offset, 0);
        DrawGlyphRun(session, state, brush);
        state.Origin = originalOrigin + new Vector2(0, -offset);
        DrawGlyphRun(session, state, brush);
        state.Origin = originalOrigin + new Vector2(0, offset);
        DrawGlyphRun(session, state, brush);
        brush.Color = originalColor;
    }

    private static void DrawGlyphRun(
        CanvasDrawingSession session,
        LyricGlyphDrawState state,
        CanvasSolidColorBrush brush)
    {
        session.DrawGlyphRun(
            state.Origin,
            state.FontFace,
            state.FontSize,
            state.Glyphs,
            state.IsSideways,
            state.BidiLevel,
            brush,
            state.MeasuringMode,
            state.LocaleName,
            state.TextString,
            state.ClusterMap,
            state.CharacterIndex);
    }
}
