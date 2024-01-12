using System;
using Windows.UI;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

public class BreathPointRenderingLyricLine : RenderingLyricLine
{
    public double BeatPerMinute { get; set; }

    private const float MinRadius = 5f;
    private const float MaxRadius = 30f;

    public override void GoToReactionState(ReactionState state, long time)
    {
        // TODO
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, long currentLyricTime)
    {
        if (currentLyricTime <= EndTime && currentLyricTime >= StartTime)
        {
            // bpm to animation duration
            var duration = 60 / BeatPerMinute * 1000; // ms
            var progress = Math.Abs(Math.Sin(Math.PI / duration * (EndTime - currentLyricTime)));

            session.FillCircle((float)offset.X + MaxRadius + 15, (float)offset.Y + MaxRadius + 15,
                MinRadius + (MaxRadius - MinRadius) * (float)progress, Colors.White);
        }
        else
        {
            session.FillCircle((float)offset.X + MaxRadius, (float)offset.Y + MaxRadius, MinRadius, Colors.Gray);
        }

        return true;
    }

    private bool _isFocusing = false;

    public bool HiddenOnBlur = true;

    public override void OnKeyFrame(CanvasDrawingSession session, long time)
    {
        _isFocusing = (time >= StartTime && time < EndTime);
        Hidden = false;
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }
    }

    public override void OnRenderSizeChanged(CanvasDrawingSession session, double width, double height, long time)
    {
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }
        RenderingHeight = MaxRadius + 80;
    }

    public override void OnTypographyChanged(CanvasDrawingSession session)
    {
        // ignore
    }
}