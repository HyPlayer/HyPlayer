#nullable enable

using Microsoft.Graphics.Canvas;
using HyPlayer.LyricRenderer.Abstraction;

namespace HyPlayer.LyricRenderer.Text;

public interface ITextHighlightEffectRenderer
{
    void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext context);
}
