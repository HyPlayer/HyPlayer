using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

/// <summary>
/// 进度条样式的BreathPoint
/// </summary>
public class ProgressBarRenderingLyricLine : RenderingLyricLine
{
    public EaseFunctionBase AnimationEaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };
    public EaseFunctionBase _easeFunc2 { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseIn };

    public int Width { get; set; } = 200;
    public int Height { get; set; } = 8;
    public int LeaveAnimationDuration { get; set; } = 600;
    public int EnterAnimationDuration { get; set; } = 400;

    private const int VerticalPadding = 4;

    private CanvasLinearGradientBrush _baseGradientBrush;
    public override void GoToReactionState(ReactionState state, RenderContext context)
    {

    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        float actualX = offset.X + SyllablesRenderingLyricLine.TextPadding;
        float actualY = offset.Y + Height;

        if (context.CurrentLyricTime > EndTime || context.CurrentLyricTime < StartTime) return true;// 未激活
        var remain = EndTime - context.CurrentLyricTime;


        // 画个底
        if (context.CurrentLyricTime - StartTime < EnterAnimationDuration)
        {
            var surplus = (float)(context.CurrentLyricTime - StartTime) / EnterAnimationDuration;
            var prog = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * prog, Height), 4, 4);
            session.FillGeometry(geometry, actualX, actualY, _baseGradientBrush);
            return true;
        }
        else if (remain > LeaveAnimationDuration)
        {
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width, Height), 4, 4);
            session.FillGeometry(geometry, actualX, actualY, _baseGradientBrush);
        }


        // 画进度
        CanvasGeometry geometryFill;
        double progress;
        var focusingColor = context.PreferTypography.FocusingColor ?? Colors.Transparent;

        if (remain < LeaveAnimationDuration)// 结束动画
        {
            var surplus = (LeaveAnimationDuration - remain) * 1.0f / (LeaveAnimationDuration);
            progress = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            focusingColor.A = (byte)(160 - 160 * progress);
            geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(Width * progress, 0, Width - Width * progress, Height), 4, 4);
        }
        else
        {
            progress = Math.Clamp((context.CurrentLyricTime - StartTime - EnterAnimationDuration) * 1.0 / (EndTime - StartTime - EnterAnimationDuration - LeaveAnimationDuration), 0, 1);
            focusingColor.A = (byte)(100 + 60 * progress);
            geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * progress, Height), 4, 4);
        }

        var cl = new CanvasCommandList(session);
        using (var clds = cl.CreateDrawingSession())
        {
            clds.FillGeometry(geometryFill, actualX, actualY, focusingColor);
        }
        session.DrawImage(cl);
        return true;
    }

    private bool _isFocusing;

    public bool HiddenOnBlur = true;

    public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        _isFocusing = (context.CurrentKeyframe >= StartTime) && (context.CurrentKeyframe < EndTime);
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
        RenderingHeight = Height + 2 * VerticalPadding;
        RenderingWidth = context.ItemWidth;
        _baseGradientBrush?.Dispose();

        var baseColor = context.PreferTypography.IdleColor ?? Colors.Transparent;
        var canvasGradientStop = new CanvasGradientStop[2];
        canvasGradientStop[0] = new CanvasGradientStop
        {
            Position = 0,
            Color = baseColor with { A = 64 }
        };

        canvasGradientStop[1] = new CanvasGradientStop()
        {
            Position = 1,
            Color = baseColor with { A = 32 }
        };

        _baseGradientBrush = new CanvasLinearGradientBrush(session, canvasGradientStop)
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(Width, 0)
        };
    }

    public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {
        // ignore
    }
}