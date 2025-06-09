using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
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
    public EaseFunctionBase LeavingEaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };
    public EaseFunctionBase ShiningEaseFunction { get; set; } = new CustomSineEase { EasingMode = EasingMode.EaseOut };
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 8;
    public int AnimationDuration { get; set; } = 800;
    public int ShineDuration { get; set; } = 1200;
    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        // TODO
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        float actualX = offset.X;
        switch (TypographySelector(t => t?.Alignment, context)!.Value)
        {
            case TextAlignment.Left:
                actualX += (context.PreferTypography.LyricFontSize! / 10).Value;
                break;
            case TextAlignment.Center:
                actualX += (float)(RenderingWidth / 2 - Width / 2.0);
                break;
            case TextAlignment.Right:
                actualX += RenderingWidth - Width;
                actualX -= (context.PreferTypography.LyricFontSize! / 10).Value + 15;
                break;
            case TextAlignment.Justify:
            case TextAlignment.DetectFromContent:
            default:
                break;
        }

        if (context.CurrentLyricTime > EndTime || context.CurrentLyricTime < StartTime) return true;//未激活

        //画个底
        var baseColor = context.PreferTypography.IdleColor!.Value;
        baseColor.A = 64;
        var geometry = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width, Height), 4, 4);
        session.FillGeometry(geometry, actualX, offset.Y + Height, baseColor);

        //画进度
        CanvasGeometry geometryFill;
        var remain = EndTime - context.CurrentLyricTime;
        double progress;
        if (remain < AnimationDuration)//结束动画
        {
            var surplus = (AnimationDuration - remain) * 1.0f / AnimationDuration;
            progress = LeavingEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(Width * progress, 0, Width - Width * progress, Height), 4, 4);
        }
        else
        {
            progress = Math.Clamp((context.CurrentLyricTime - StartTime) * 1.0f / (EndTime - StartTime - AnimationDuration - ShineDuration), 0, 1);
            geometryFill = CanvasGeometry.CreateRoundedRectangle(session, new Rect(0, 0, Width * progress, Height), 4, 4);
        }

        var cl = new CanvasCommandList(session);
        var focusingColor = context.PreferTypography.FocusingColor!.Value;
        focusingColor.A = (byte)(100 + 60 * progress);
        using (var clds = cl.CreateDrawingSession())
        {
            clds.FillGeometry(geometryFill, actualX, offset.Y + Height, focusingColor);
        }

        //发光效果
        if (remain < AnimationDuration + ShineDuration)
        {
            var surplus = ShiningEaseFunction.Ease(Math.Clamp(1.0f * (AnimationDuration + ShineDuration - remain) / AnimationDuration, 0, 1));
            if (remain < AnimationDuration)//结束动画
            {
                surplus = Math.Clamp(1 - (AnimationDuration - remain) * 1.0f / AnimationDuration, 0, 1);
            }
            var blur = new ShadowEffect
            {
                Source = cl,
                BlurAmount = 10,
                ShadowColor = context.PreferTypography.ShadowColor ?? context.PreferTypography.ShadowColor!.Value,
            };
            var opacity = new OpacityEffect
            {
                Source = blur,
                Opacity = ((float)surplus) * 0.8F,
            };
            session.DrawImage(opacity);
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