#nullable enable

using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public sealed class RectRevealScanEffectRenderer : ITextHighlightEffectRenderer
{
    private readonly TokenLiftGlyphEffect _liftEffect = new();
    private readonly RectRevealCalculator _revealCalculator = new();

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
            var effectContext = new LyricGlyphEffectContext(context, layout, frame, cluster);
            var baseState = LyricGlyphDrawState.FromCluster(cluster, layout.FocusingColor);
            _liftEffect.Apply(effectContext, ref baseState);
            var transliterationScanDisabled = cluster.Layer == LyricTextLayer.Transliteration &&
                                              !context.Effects.TransliterationScanning;
            baseState.Opacity = transliterationScanDisabled ? 1 : context.Specs.UnscannedTextOpacity;
            GlyphRunDrawHelper.DrawCluster(session, brush, baseState);

            if (transliterationScanDisabled)
            {
                continue;
            }

            var revealProgress = _revealCalculator.GetRevealProgress(cluster, frame);
            if (revealProgress <= 0)
            {
                continue;
            }

            var highlightState = baseState;
            highlightState.Opacity = 1;
            highlightState.Color = layout.FocusingColor;
            var shouldDrawScanGlow = context.Effects.FocusHighlighting &&
                                     context.Specs.ScanGlowRadius > 0.001f &&
                                     context.Specs.ScanGlowOpacity > 0.001f;
            if (revealProgress >= 1)
            {
                GlyphRunDrawHelper.DrawCluster(session, brush, highlightState);
                continue;
            }

            DrawHighlightedCluster(session, brush, cluster, highlightState, revealProgress,
                context.Specs.RevealFeather, shouldDrawScanGlow, context.Specs.ScanGlowRadius,
                context.Specs.ScanGlowOpacity, context.Specs.ScanGlowEdgeWidth);
        }
    }

    private void DrawHighlightedCluster(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphCluster cluster,
        LyricGlyphDrawState highlightState,
        float revealProgress,
        float feather,
        bool shouldDrawScanGlow,
        float glowRadius,
        float glowOpacity,
        float glowEdgeWidth)
    {
        var revealWidth = cluster.VisualWidth * revealProgress;
        if (shouldDrawScanGlow)
        {
            DrawSweepGlow(session, brush, cluster, highlightState, revealWidth, glowRadius, glowOpacity,
                glowEdgeWidth);
        }

        if (feather <= 0.001f)
        {
            var clip = _revealCalculator.GetRevealClip(cluster, highlightState, revealProgress);
            if (clip is not null)
            {
                GlyphRunDrawHelper.DrawCluster(session, brush, highlightState, clip);
            }

            return;
        }

        var hardWidth = revealWidth - feather;
        if (hardWidth > 0)
        {
            var hardClip = _revealCalculator.GetRevealClip(cluster, highlightState, 0, hardWidth);
            if (hardClip is not null)
            {
                GlyphRunDrawHelper.DrawCluster(session, brush, highlightState, hardClip);
            }
        }

        const int steps = 5;
        var stepWidth = feather / steps;
        for (var i = 0; i < steps; i++)
        {
            var start = Math.Max(0, hardWidth) + stepWidth * i;
            if (start >= revealWidth)
            {
                break;
            }

            var featherState = highlightState;
            featherState.Opacity = (steps - i) * 1f / (steps + 1);
            var featherClip = _revealCalculator.GetRevealClip(cluster, featherState, start,
                Math.Min(stepWidth, revealWidth - start));
            if (featherClip is not null)
            {
                GlyphRunDrawHelper.DrawCluster(session, brush, featherState, featherClip);
            }
        }
    }

    private static void DrawSweepGlow(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphCluster cluster,
        LyricGlyphDrawState highlightState,
        float revealWidth,
        float glowRadius,
        float glowOpacity,
        float glowEdgeWidth)
    {
        if (revealWidth <= 0)
        {
            return;
        }

        var width = Math.Max(1, glowEdgeWidth);
        var radius = Math.Max(0, glowRadius);
        var centerX = cluster.VisualLeft + revealWidth;
        var offsetX = highlightState.Origin.X - cluster.BaseState.Origin.X;
        var offsetY = highlightState.Origin.Y - cluster.BaseState.Origin.Y;
        var top = cluster.VisualTop + offsetY - radius;
        var height = cluster.VisualHeight + radius * 2;
        if (height <= 0)
        {
            return;
        }

        var glowState = highlightState;
        glowState.BlurRadius = radius;

        glowState.Opacity = glowOpacity * 0.55f;
        GlyphRunDrawHelper.DrawCluster(session, brush, glowState, new Rect(
            centerX + offsetX - width * 0.5f - radius,
            top,
            width + radius * 2,
            height));

        glowState.Opacity = glowOpacity;
        GlyphRunDrawHelper.DrawCluster(session, brush, glowState, new Rect(
            centerX + offsetX - width * 0.5f,
            top,
            width,
            height));
    }
}
