#nullable enable
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
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
    public bool IsPlayed { get; private set; }
    private CanvasTransition _blurTransition = new CanvasTransition { Duration = TimeSpan.FromSeconds(0.5), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
    private CanvasTransition _opacityTransition = new CanvasTransition { Duration = TimeSpan.FromSeconds(1), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
    private CanvasTransition _scaleTranslation = new CanvasTransition { Duration = TimeSpan.FromSeconds(0.25), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
    private CanvasTransition _transform3dTranslation = new CanvasTransition { Duration = TimeSpan.FromSeconds(0.5), Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };
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

        if (context.Effects.Transform3D)
        {
            var targetAngle = IsActive ? 0f : Math.Clamp(-(float)(gap * 15), -60f, 60f);
            if (context.IsScrolling) targetAngle = 0;
            var angle = _transform3dTranslation.Animate(context.CurrentLyricTime, targetAngle);
            effectBuilder.AddTransform3DEffect(
                Get3DMatrix(new(-(float)offset.X, 0, 0),
                angleY: angle, depth: 2500f));
        }

        if (context.Effects.Blur && !IsActive && !context.IsScrolling)
        {
            var blur = Math.Clamp(gap, 0, 250);
            if (context.IsScrolling) blur = 0;
            effectBuilder.AddGaussianBlurEffect(_blurTransition.Animate(context.CurrentLyricTime, blur));
        }

        if (context.Effects.Fade)
        {
            var opacity = Math.Clamp(0.5 - (gap / (10f - (context.Effects.FadingRatio / 10f))), 0, 1);
            if (gap == 0) opacity = 1;
            if (context.IsScrolling) opacity = Math.Max(opacity, 0.5);
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

    protected Matrix4x4 Get3DMatrix(Vector3 center, float angleX = 0,
        float angleY = 0, float angleZ = 0, float depth = 800f)
    {
        var parallaxTranslation = Matrix4x4.Identity;


        var rotationX = (float)(Math.PI * angleX / 180.0);
        var rotationY = (float)(Math.PI * angleY / 180.0);
        var rotationZ = (float)(Math.PI * angleZ / 180.0);

        var rotation = Matrix4x4.CreateRotationX(rotationX) *
                       Matrix4x4.CreateRotationY(rotationY) *
                       Matrix4x4.CreateRotationZ(rotationZ);

        var perspective = Matrix4x4.Identity;
        if (depth > 0) perspective.M34 = 1.0f / depth;

        return Matrix4x4.CreateTranslation(-center) * rotation * perspective *
                         Matrix4x4.CreateTranslation(center) * parallaxTranslation;
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

public enum ReactionState
{
    Leave,
    Enter,
    Press
}
