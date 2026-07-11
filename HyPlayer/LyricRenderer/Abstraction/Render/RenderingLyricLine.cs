#nullable enable
using HyPlayer.LyricRenderer.Builder;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Abstraction.Render;

public abstract class RenderingLyricLine : IDisposable
{
    public int Id { get; set; }
    public RenderTypography? Typography { get; set; }
    public float RenderingHeight { get; set; }
    public float RenderingWidth { get; set; }
    public bool Rendering { get; set; } = false;

    public ReactionState ReactionState { get; set; }

    public bool Hidden { get; set; }

    public List<long>? KeyFrames { get; set; }

    public long StartTime { get; set; }
    public long EndTime { get; set; }

    public bool IsActive { get; private set; }

    public bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        using var commandList = new CanvasCommandList(session);

        var result = RenderCore(commandList, context);
        var effectBuilder = new CanvasImageBuilder(commandList);

        //if (context.Effects.ScaleWhenFocusing)
        //{
        //    var progress = 0f;
        //    if (context.CurrentLyricTime - EndTime >= 0 &&
        //        context.CurrentLyricTime - EndTime <= ScaleAnimationDuration)
        //    {
        //        progress = 1 - (float)EaseFunction.Ease(Math.Clamp(
        //            (context.CurrentLyricTime - EndTime) * 1.0f / ScaleAnimationDuration, 0, 1));
        //    }
        //    else if (_isFocusing && context.CurrentLyricTime - StartTime >= 0)
        //    {
        //        progress = (float)_elasticEase.Ease(Math.Clamp(
        //            (context.CurrentLyricTime - StartTime) * 1.0f / 1000, 0, 1));
        //    }

        //    var scaling = 0.8F + progress * 0.2F;
        //    finalEffectBuilder
        //        .AddTransform2DEffect(GetCenterMatrix(0, 0, actualOffsetX + _layout.ScalingCenterX,
        //            (float)_layout.TextLayout.LayoutBounds.Height / 2, scaling, scaling))
        //        .AddOpacityEffect(Math.Clamp(0.5f + progress * 0.5f, 0, 1));
        //}
        var gap = IsActive ? 0 : Math.Clamp(Math.Abs(Id - context.CurrentLyricLineIndex), 1, 250);

        if (context.Effects.Blur && !IsActive && !context.IsScrolling)
        {
            effectBuilder.AddGaussianBlurEffect(Math.Clamp(gap, 0, 250));
        }

        if (context.Effects.Fade && !context.IsScrolling)
        {
            effectBuilder.AddOpacityEffect(1 -
                Math.Clamp(gap / (10f - (5 / 10f)), 0, 0.9f));
        }

        session.DrawImage(effectBuilder.Build(), 0, offset.Y);
        if (ReactionState == ReactionState.Enter && !Hidden)
        {
            session.FillRoundedRectangle(offset.X, offset.Y,
                RenderingWidth + 2, RenderingHeight + 8, 6, 6,
                Color.FromArgb(10, 255, 255, 255));
        }

        if (context.Debug)
        {
            session.DrawText($"(X{offset.X},Y{offset.Y},W{RenderingWidth},H{RenderingHeight})", offset.X, offset.Y, Colors.Red);
            session.DrawText(Id.ToString(), offset.X, offset.Y + 15, Colors.Red);
            session.DrawRectangle(offset.X, offset.Y, RenderingWidth, RenderingHeight, Colors.Yellow);
        }
        return result;
    }

    public void GoToReactionState(ReactionState state, RenderContext context)
    {
        ReactionState = state;
    }
    protected abstract bool RenderCore(CanvasCommandList commandList, RenderContext context);

    public void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        IsActive = context.CurrentKeyframe >= StartTime && context.CurrentKeyframe < EndTime;
        OnKeyFrameCore(session, context);
    }
    protected virtual void OnKeyFrameCore(CanvasDrawingSession session, RenderContext context)
    {

    }
    public virtual void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
    {

    }
    public virtual void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {

    }
    public virtual void Dispose()
    {
    }

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
