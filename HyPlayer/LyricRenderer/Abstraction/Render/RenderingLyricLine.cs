#nullable enable
using System.Collections.Generic;
using Microsoft.Graphics.Canvas;
using System;

namespace HyPlayer.LyricRenderer.Abstraction.Render;

public abstract class RenderingLyricLine
{
    public int Id { get; set; }
    public RenderTypography Typography { get; set; }
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
    public T TypographySelector<T>(Func<RenderTypography?, T?> expression, RenderContext context)
    {
        return (expression(Typography) ??
                expression(context.PreferTypography) ?? expression(RenderTypography.Default))!;
    }
}

public enum ReactionState
{
    Leave,
    Enter,
    Press
}