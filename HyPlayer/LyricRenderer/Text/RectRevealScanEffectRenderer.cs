#nullable enable

using System.Collections.Generic;
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;

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
            DrawClusters(session, brush, layout.TransliterationGlyphClusters, layout, frame, context);
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
            baseState.Opacity = transliterationScanDisabled ? 1 : 0.3f;
            GlyphRunDrawHelper.DrawCluster(session, brush, baseState);

            if (transliterationScanDisabled) continue;

            var revealProgress = _revealCalculator.GetRevealProgress(cluster, frame);
            if (revealProgress <= 0) continue;

            var highlightState = baseState;
            highlightState.Opacity = 1;
            highlightState.Color = layout.FocusingColor;
            if (revealProgress >= 1)
            {
                GlyphRunDrawHelper.DrawCluster(session, brush, highlightState);
                continue;
            }

            var clip = _revealCalculator.GetRevealClip(cluster, highlightState, revealProgress);
            if (clip is null) continue;
            GlyphRunDrawHelper.DrawCluster(session, brush, highlightState, clip);
        }
    }
}