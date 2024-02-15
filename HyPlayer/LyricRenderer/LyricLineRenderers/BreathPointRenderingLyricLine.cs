using System;
using Windows.UI;
using Windows.UI.Xaml;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

public class BreathPointRenderingLyricLine : RenderingLyricLine
{
    public float BeatPerMinute { get; set; }

    private const float MinRadius = 5f;
    private const float MaxRadius = 30f;

    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        // TODO
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        float actualX = offset.X;
        switch(context.PreferTypography.Alignment)
        {
            case TextAlignment.Left:
                actualX += MaxRadius / 2;
                break;
            case TextAlignment.Center:
                actualX += RenderingWidth / 2 - MaxRadius;
                break;
            case TextAlignment.Right:
                actualX += RenderingWidth - MaxRadius * 2;
                break;
        }   
        if (context.CurrentLyricTime <= EndTime && context.CurrentLyricTime >= StartTime)
        {
            // bpm to animation duration
            var duration = 60 / BeatPerMinute * 1000; // ms
            var progress = Math.Abs(Math.Sin(Math.PI / duration * (EndTime - context.CurrentLyricTime)));

            session.FillCircle(actualX + 15, offset.Y + MaxRadius + 15,
                MinRadius + (MaxRadius - MinRadius) * (float)progress, Colors.White);
        }
        else
        {
            session.FillCircle(actualX, offset.Y + MaxRadius, MinRadius, Colors.Gray);
        }

        return true;
    }

    private bool _isFocusing = false;

    public bool HiddenOnBlur = true;

    public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        _isFocusing = (context.CurrentKeyframe >= StartTime && context.CurrentKeyframe < EndTime);
        Hidden = false;
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }
    }

    public override void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
    {
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }
        RenderingHeight = MaxRadius + 80;
        RenderingWidth = context.ItemWidth;
    }

    public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {
        // ignore
    }
}