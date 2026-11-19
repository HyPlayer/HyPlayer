using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.UI.Effects;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

/// <summary>
///     进度条样式的BreathPoint
/// </summary>
public partial class ProgressBarRenderingLyricLine : RenderingLyricLine
{
    private const int VerticalPadding = 4;

    private CanvasLinearGradientBrush _baseGradientBrush;

    public EaseFunctionBase AnimationEaseFunction { get; set; } =
        new CustomCircleEase { EasingMode = EasingMode.EaseOut };

    public EaseFunctionBase _easeFunc2 { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseIn };

    public int Width { get; set; } = 200;
    public int Height { get; set; } = 8;
    public int LeaveAnimationDuration { get; set; } = 600;
    public int EnterAnimationDuration { get; set; } = 400;

    protected override bool RenderCore(CanvasDrawingSession session, RenderContext context)
    {
        var x = TextRenderingLyricLine.TextPadding;
        float y = Height;

        if (context.CurrentLyricTime > EndTime || context.CurrentLyricTime < StartTime) return true; // 未激活
        var remain = EndTime - context.CurrentLyricTime;

        // 画个底
        if (context.CurrentLyricTime - StartTime < EnterAnimationDuration)
        {
            var surplus = (float)(context.CurrentLyricTime - StartTime) / EnterAnimationDuration;
            var prog = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            session.FillRoundedRectangle(new Rect(x, y, Width * prog, Height), 4, 4, _baseGradientBrush);
            return true;
        }

        if (remain > LeaveAnimationDuration)
            session.FillRoundedRectangle(new Rect(x, y, Width, Height), 4, 4, _baseGradientBrush);


        // 画进度
        Rect fill;
        double progress;
        var focusingColor = context.PreferTypography.FocusingColor!.Value;

        if (remain < LeaveAnimationDuration) // 结束动画
        {
            var surplus = (LeaveAnimationDuration - remain) * 1.0f / LeaveAnimationDuration;
            progress = AnimationEaseFunction.Ease(Math.Clamp(surplus, 0, 1));
            focusingColor.A = (byte)(160 - 160 * progress);
            fill = new Rect(x + Width * progress, y, Width - Width * progress, Height);
        }
        else
        {
            progress = Math.Clamp(
                (context.CurrentLyricTime - StartTime - EnterAnimationDuration) * 1.0 /
                (EndTime - StartTime - EnterAnimationDuration - LeaveAnimationDuration), 0, 1);
            focusingColor.A = (byte)(100 + 60 * progress);
            fill = new Rect(x, y, Width * progress, Height);
        }

        session.FillRoundedRectangle(fill, 4, 4, focusingColor);
        return true;
    }

    protected override bool HasContent(RenderContext context) =>
        context.CurrentLyricTime >= StartTime && context.CurrentLyricTime <= EndTime;

    public override void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
    {
        RenderingHeight = Height + 2 * VerticalPadding;
        RenderingWidth = Width + 2 * TextRenderingLyricLine.TextPadding;
        _baseGradientBrush?.Dispose();

        var baseColor = context.PreferTypography.IdleColor!.Value;
        var canvasGradientStop = new CanvasGradientStop[2];
        canvasGradientStop[0] = new CanvasGradientStop
        {
            Position = 0,
            Color = baseColor with { A = 64 }
        };

        canvasGradientStop[1] = new CanvasGradientStop
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
        ReleaseDrawingResources();
        OnRenderSizeChanged(session, context);
    }

    public override void Dispose()
    {
        _baseGradientBrush?.Dispose();
        _baseGradientBrush = null;
        base.Dispose();
    }
}
