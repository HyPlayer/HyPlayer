using System;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.Classes;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

/// <summary>
/// 进度条样式的BreathPoint
/// </summary>
public class ProgressBarRenderingLyricLine : RenderingLyricLine
{
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 8;
    public int AnimationDuration { get; set; } = 800;
    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        // TODO
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        float actualX = (float)offset.X;
        switch (context.PreferTypography.Alignment)
        {
            case TextAlignment.Left:
                actualX += 8;
                break;
            case TextAlignment.Center:
                actualX += (float)(RenderingWidth / 2 - Width / 2.0);
                break;
            case TextAlignment.Right:
                actualX += (float)(RenderingWidth - Width);
                actualX -= 12;
                break;
        }
        if (context.CurrentLyricTime <= EndTime && context.CurrentLyricTime >= StartTime)
        {          
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width, Height), 4, 4);
            session.FillGeometry(geometry, actualX, (float)offset.Y+Height, Color.FromArgb(64, 255, 255, 255));

            var value = (double)(context.CurrentLyricTime - StartTime) / (EndTime - StartTime - AnimationDuration);

            if ((EndTime - context.CurrentLyricTime)< AnimationDuration)//结束动画
            {
                var surplus = (double)(AnimationDuration - (EndTime - context.CurrentLyricTime)) / AnimationDuration;
                var progress = EaseFunction.Ease(Math.Clamp(surplus, 0, 1));
                var geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(Width * progress, 0, Width - Width * progress, Height), 4, 4);
                session.FillGeometry(geometryFill, actualX, (float)offset.Y + Height, Colors.White);
            }
            else
            {
                var progress = Math.Clamp(value , 0, 1);
                var geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * progress, Height), 4, 4);
                session.FillGeometry(geometryFill, actualX, (float)offset.Y + Height, Colors.White);
            }
        }

        return true;
    }

    private bool _isFocusing;

    public bool HiddenOnBlur = true;

    public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        _isFocusing = (context.CurrentLyricTime >= StartTime && context.CurrentLyricTime < EndTime);
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
        RenderingHeight = Height;
        RenderingWidth = context.ItemWidth;
    }

    public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {
        // ignore
    }
}