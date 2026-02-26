using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

/// <summary>
/// 进度条样式的BreathPoint
/// </summary>
public class ProgressBarRenderingLyricLine : RenderingLyricLine
{
    public EaseFunctionBase AnimationEaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 8;
    public int LeaveAnimationDuration { get; set; } = 800;
    public int EnterAnimationDuration { get; set; } = 400;
    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        // TODO
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        float actualX = offset.X + SyllablesRenderingLyricLine.TextPadding;

        if (context.CurrentLyricTime > EndTime || context.CurrentLyricTime < StartTime) return true;// 未激活
        var remain = EndTime - context.CurrentLyricTime;


        // 画个底
        var baseColor = context.PreferTypography.IdleColor!.Value;
        baseColor.A = 64;

        if (remain < LeaveAnimationDuration)// 结束动画
        {
            var surplus = (LeaveAnimationDuration - remain) * 1.0f / LeaveAnimationDuration;
            var prog = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            baseColor.A = (byte)(64 - 64 * prog);
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(Width * prog, 0, Width - Width * prog, Height), 4, 4);
            session.FillGeometry(geometry, actualX, offset.Y + Height, baseColor);
        }
        else if (context.CurrentLyricTime - StartTime < EnterAnimationDuration)
        {
            var surplus = (float)(context.CurrentLyricTime - StartTime) / EnterAnimationDuration;
            var prog = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            baseColor.A = (byte)(64 * prog);
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * prog, Height), 4, 4);
            session.FillGeometry(geometry, actualX, offset.Y + Height, baseColor);
            return true;
        }
        else
        {
            var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width, Height), 4, 4);
            session.FillGeometry(geometry, actualX, offset.Y + Height, baseColor);
        }


        // 画进度
        CanvasGeometry geometryFill;
        double progress;
        var focusingColor = context.PreferTypography.FocusingColor!.Value;

        if (remain < LeaveAnimationDuration * 1.2)// 结束动画
        {
            var surplus = (LeaveAnimationDuration * 1.2 - remain) * 1.0f / (LeaveAnimationDuration * 1.2);
            progress = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            focusingColor.A = (byte)(160 - 160 * progress);
            geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(Width * progress, 0, Width - Width * progress, Height), 4, 4);
        }
        else
        {
            progress = Math.Clamp((context.CurrentLyricTime - StartTime) * 1.0f / (EndTime - StartTime - EnterAnimationDuration - LeaveAnimationDuration * 1.2), 0, 1);
            focusingColor.A = (byte)(100 + 60 * progress);
            geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * progress, Height), 4, 4);
        }

        var cl = new CanvasCommandList(session);
        using (var clds = cl.CreateDrawingSession())
        {
            clds.FillGeometry(geometryFill, actualX, offset.Y + Height, focusingColor);
        }
        session.DrawImage(cl);
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