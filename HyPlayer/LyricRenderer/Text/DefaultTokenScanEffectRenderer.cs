#nullable enable

using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public class DefaultTokenScanEffectRenderer : ITextHighlightEffectRenderer
{
    private readonly IReadOnlyList<ILyricGlyphEffect> _effects;

    public DefaultTokenScanEffectRenderer()
        : this([new TokenScanGlyphEffect()])
    {
    }

    public DefaultTokenScanEffectRenderer(IReadOnlyList<ILyricGlyphEffect> effects)
    {
        _effects = effects;
    }

    public void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext context)
    {
        using var brush = new CanvasSolidColorBrush(session, layout.FocusingColor);
        DrawClusters(session, brush, layout.LyricGlyphClusters, layout, frame, context);
        if (context.EnableTransliteration && layout.TransliterationGlyphClusters.Count > 0)
        {
            DrawClusters(session, brush, layout.TransliterationGlyphClusters, layout, frame, context);
        }
    }

    private void DrawClusters(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        IReadOnlyList<LyricGlyphCluster> clusters,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext context)
    {
        for (var i = 0; i < clusters.Count; i++)
        {
            var cluster = clusters[i];
            var state = LyricGlyphDrawState.FromCluster(cluster, layout.FocusingColor);
            var effectContext = new LyricGlyphEffectContext(context, layout, frame, cluster);
            for (var effectIndex = 0; effectIndex < _effects.Count; effectIndex++)
            {
                _effects[effectIndex].Apply(effectContext, ref state);
            }

            DrawCluster(session, brush, state);
        }
    }

    private static void DrawCluster(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state)
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

            if (state.BlurRadius > 0.001f)
            {
                DrawBlurApproximation(session, brush, state);
            }

            DrawGlyphRun(session, state, brush);
        }
        finally
        {
            session.Transform = originalTransform;
        }
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
