#nullable enable

using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.Domain;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Text;

public class DefaultTokenScanEffectRenderer : ITextHighlightEffectRenderer
{
    private readonly ITextHighlightEffectRenderer _rectRevealRenderer;
    private readonly ITextHighlightEffectRenderer _tokenOpacityRenderer;

    public DefaultTokenScanEffectRenderer()
        : this(new RectRevealScanEffectRenderer(), new TokenOpacityScanEffectRenderer())
    {
    }

    public DefaultTokenScanEffectRenderer(
        ITextHighlightEffectRenderer rectRevealRenderer,
        ITextHighlightEffectRenderer tokenOpacityRenderer)
    {
        _rectRevealRenderer = rectRevealRenderer;
        _tokenOpacityRenderer = tokenOpacityRenderer;
    }

    public void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext context)
    {
        if (context.Effects.ScanStyle == LyricScanStyle.TokenOpacity)
        {
            _tokenOpacityRenderer.Render(session, layout, frame, context);
            return;
        }

        _rectRevealRenderer.Render(session, layout, frame, context);
    }
}
