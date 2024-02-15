using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Abstraction.Render;

public abstract class RenderingLyricLine
{
    public int Id { get; set; }

    public float RenderingHeight { get; set; }
    public float RenderingWidth { get; set; }

    public bool Hidden { get; set; }

    public List<long> KeyFrames { get; set; }

    public long StartTime { get; set; }
    public long EndTime { get; set; }

    public abstract void GoToReactionState(ReactionState state, RenderContext context);
    public abstract bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context);
    public abstract void OnKeyFrame(CanvasDrawingSession session, RenderContext context);
    public abstract void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context);
    public abstract void OnTypographyChanged(CanvasDrawingSession session, RenderContext context);
}

public enum ReactionState
{
    Leave,
    Enter,
    Press
}