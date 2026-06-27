#nullable enable

using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace HyPlayer.LyricRenderer.Text;

public interface ITextHighlightEffectRenderer
{
    void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        Rect sizePixelRect,
        float textTop);
}
