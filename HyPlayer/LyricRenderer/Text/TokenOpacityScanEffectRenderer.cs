#nullable enable

using System.Collections.Generic;
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;

namespace HyPlayer.LyricRenderer.Text;

public sealed class TokenOpacityScanEffectRenderer(IReadOnlyList<ILyricGlyphEffect> effects)
    : ITextHighlightEffectRenderer
{
    public TokenOpacityScanEffectRenderer()
        : this((ILyricGlyphEffect[])[new TokenLiftGlyphEffect(), new TokenOpacityGlyphEffect()])
    {
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
            var state = LyricGlyphDrawState.FromCluster(cluster, layout.FocusingColor);
            var effectContext = new LyricGlyphEffectContext(context, layout, frame, cluster);
            for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                effects[effectIndex].Apply(effectContext, ref state);

            GlyphRunDrawHelper.DrawCluster(session, brush, state);
        }
    }
}