#nullable enable

using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Text;

public interface ITextHighlightEffectRenderer
{
    void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext context);
}