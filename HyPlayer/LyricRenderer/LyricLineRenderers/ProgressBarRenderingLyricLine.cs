using System;
using Windows.UI;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using HyPlayer.Classes;
using Windows.UI.Xaml.Media.Animation;
using System.Diagnostics;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

/// <summary>
/// 进度条样式的BreathPoint
/// </summary>
public class ProgressBarRenderingLyricLine : RenderingLyricLine
{
    private const float MinRadius = 5f;
    private const float MaxRadius = 30f;
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 8;
    public override void GoToReactionState(ReactionState state, long time)
    {
        // TODO
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, long currentLyricTime, long renderingTick, int gap)
    {
        float actualX = (float)offset.X;
        switch (TextAlignment)
        {
            case Windows.UI.Xaml.TextAlignment.Left:
                actualX += MaxRadius / 2;
                break;
            case Windows.UI.Xaml.TextAlignment.Center:
                actualX += (float)(RenderingWidth / 2 - MaxRadius);
                break;
            case Windows.UI.Xaml.TextAlignment.Right:
                actualX += (float)(RenderingWidth - MaxRadius * 2);
                break;
        }
        if (currentLyricTime <= EndTime && currentLyricTime >= StartTime)
        {          
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width, Height), 4, 4);
            session.FillGeometry(geometry, (float)offset.X + 4, (float)offset.Y + MaxRadius, Color.FromArgb(64, 255, 255, 255));

            var value = (double)(currentLyricTime - StartTime) / (EndTime - StartTime);

            if ((EndTime - currentLyricTime)< 1000)//结束动画
            {
                var surplus = (double)(1000 - (EndTime - currentLyricTime)) / 1000;
                var progress = EaseFunction.Ease(Math.Clamp(surplus, 0, 1));
                var geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(Width * progress, 0, Width - Width * progress, Height), 4, 4);
                session.FillGeometry(geometryFill, (float)offset.X + 4, (float)offset.Y + MaxRadius, Colors.White);
            }
            else
            {
                var progress = EaseFunction.Ease(Math.Clamp(value, 0, 1));
                var geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * progress, Height), 4, 4);
                session.FillGeometry(geometryFill, (float)offset.X + 4, (float)offset.Y + MaxRadius, Colors.White);
            }
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
        RenderingHeight = MaxRadius;
        RenderingWidth = width;
    }

    public override void OnTypographyChanged(CanvasDrawingSession session)
    {
        // ignore
    }
}