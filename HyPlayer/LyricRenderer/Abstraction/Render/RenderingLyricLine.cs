#nullable enable
using HyPlayer.Domain.Lyrics;
using HyPlayer.LyricRenderer.Builder;
using Impressionist.Helpers;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.UI;
using Windows.UI.Xaml.Media.Animation;

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
    private Transition _blurTransition = new Transition { Duration = TimeSpan.FromSeconds(0.5), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
    private Transition _opacityTransition = new Transition { Duration = TimeSpan.FromSeconds(1), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
    private Transition _scaleTranslation = new Transition { Duration = TimeSpan.FromSeconds(0.25), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
    public bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        using var commandList = new CanvasCommandList(session);

        var result = RenderCore(commandList, context);
        var effectBuilder = new CanvasImageBuilder(commandList);

        if (context.Effects.ScaleWhenFocusing)
        {
            var scaling = _scaleTranslation.Animate(context.CurrentLyricTime, IsActive ? 1f : 0.9f);

            effectBuilder
                .AddTransform2DEffect(GetCenterMatrix(0, 0, offset.X,
                    RenderingHeight / 2, scaling, scaling));
        }
        var gap = IsActive ? 0 : Math.Clamp(Math.Abs(Id - context.CurrentLyricLineIndex), 1, 250);

        if (context.Effects.Blur && !IsActive && !context.IsScrolling)
        {
            var blur = Math.Clamp(gap, 0, 250);
            if (context.IsScrolling) blur = 0;
            effectBuilder.AddGaussianBlurEffect(_blurTransition.Animate(context.CurrentLyricTime, blur));
        }

        if (context.Effects.Fade)
        {
            var opacity = Math.Clamp(0.6 - (gap / (10f - (20 / 10f))), 0, 1);
            if (gap == 0 || context.IsScrolling) opacity = 1;
            effectBuilder.AddOpacityEffect(_opacityTransition.Animate(context.CurrentLyricTime, opacity));
        }

        session.DrawImage(effectBuilder.Build(), offset.X, offset.Y);
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
    public static Matrix3x2 GetCenterMatrix(float x, float y, float xCenter, float yCenter, float xScale, float yScale)
    {
        return Matrix3x2.CreateTranslation(-xCenter, -yCenter)
               * Matrix3x2.CreateScale(xScale, yScale)
               * Matrix3x2.CreateTranslation(x, y)
               * Matrix3x2.CreateTranslation(xCenter, yCenter);
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

public class Transition
{
    public TimeSpan Duration { get; set; }
    public EaseFunctionBase Easing { get; set; }
    private double _startValue;
    private double _targetValue = double.NaN;
    private double _startTime = double.MinValue;

    public float Animate(long currentTime, double value)
    {
        if (double.IsNaN(_targetValue))
        {
            // First call: jump directly to the initial value with no animation.
            _startValue = value;
            _targetValue = value;
            _startTime = currentTime;
            return (float)value;
        }

        if (Math.Abs(value - _targetValue) > double.Epsilon)
        {
            // Target changed mid-animation: capture current interpolated position as
            // the new start so the transition continues smoothly from here.
            var currentProgress = Math.Clamp((currentTime - _startTime) / Duration.TotalMilliseconds, 0, 1);
            _startValue = _startValue + (_targetValue - _startValue) * Easing.Ease(currentProgress);
            _targetValue = value;
            _startTime = currentTime;
        }

        var progress = Math.Clamp((currentTime - _startTime) / Duration.TotalMilliseconds, 0, 1);
        return (float)(_startValue + (_targetValue - _startValue) * Easing.Ease(progress));
    }
}

public enum ReactionState
{
    Leave,
    Enter,
    Press
}
