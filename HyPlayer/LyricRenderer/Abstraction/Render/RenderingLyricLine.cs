#nullable enable
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.Builder;
using HyPlayer.LyricRenderer.Effect;
using Impressionist.Helpers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Graphics.Effects;
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

    public List<LyricEffect> Effects { get; set; }
    = [
        new LyricBlurEffect{
            Amount  = new((lyricLine, context) =>
            {
                var blur = GetGapValue(lyricLine, context, 0f, 15f);
                blur = (context.IsScrolling) ? 0 : blur;
                return blur;
            })
        },
        new LyricOpacityEffect{
            Opacity = new((lyricLine, context) =>
            {
                var opacity = Math.Clamp(GetGapValue(lyricLine, context, 1f, 0f),0,1);
                if (context.IsScrolling) opacity = MathF.Max(opacity, 0.6F);
                return (float)opacity;
            })
        }];
    public List<LyricEffect> FinalEffects { get; set; } = [
        new LyricTransform2DEffect
        {
            XScale = new((lyricLine, context) =>
            {
                var scale = GetGapValue(lyricLine, context, 1f, 0.5f);
                if (context.IsScrolling) scale = Math.Max(scale, 0.8f);
                return scale;
            }),
            YScale = new((lyricLine, context) =>
            {
                var scale = GetGapValue(lyricLine, context, 1f, 0.5f);
                if (context.IsScrolling) scale = Math.Max(scale, 0.8f);
                return scale;
            })
        }];

    private static float GetGapValue(RenderingLyricLine lyricLine, RenderContext context, float start, float target)
    {
        var gap = Math.Abs((context.RenderOffsets[context.CurrentLyricLineIndex].Y - context.RenderOffsets[lyricLine.Id].Y) / context.ViewHeight);
        if (lyricLine.IsActive) gap = 0;
        var value = start + (target - start) * gap;
        return value;
    }

    public bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        using var commandList = new CanvasCommandList(session);
        bool result;
        using (var currentLineSession = commandList.CreateDrawingSession())
        {
            result = RenderCore(currentLineSession, context);
        }

        var gap = IsActive ? 0 : Math.Clamp(Math.Abs(Id - context.CurrentLyricLineIndex), 1, 250);

        ICanvasImage ef = commandList;
        foreach (var effect in Effects)
        {
            ef = effect.Apply(ef, this, context);
        }


        var compositeList = new CanvasCommandList(session);
        using (var compositeSession = compositeList.CreateDrawingSession())
        {
            compositeSession.DrawImage(ef);

            if (ReactionState == ReactionState.Enter && !Hidden)
            {
                compositeSession.FillRoundedRectangle(0, 0,
                    RenderingWidth + 2, RenderingHeight + 8, 6, 6,
                    Color.FromArgb(10, 255, 255, 255));
            }

            if (context.Debug)
            {
                compositeSession.DrawText($"(X{offset.X},Y{offset.Y},W{RenderingWidth},H{RenderingHeight})", 0, 0, Colors.Red);
                compositeSession.DrawText(Id.ToString(), 0, 15, Colors.Red);
                compositeSession.DrawRectangle(0, 0, RenderingWidth, RenderingHeight, Colors.Yellow);
            }
        }


        ICanvasImage finalEffect = compositeList;
        foreach (var effect in FinalEffects)
        {
            finalEffect = effect.Apply(finalEffect, this, context);
        }

        session.DrawImage(finalEffect, offset.X, offset.Y);

        return result;
    }



    public void GoToReactionState(ReactionState state, RenderContext context)
    {
        ReactionState = state;
    }
    protected abstract bool RenderCore(CanvasDrawingSession session, RenderContext context);

    public void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        IsActive = context.CurrentKeyframe >= StartTime && context.CurrentKeyframe < EndTime;
        IsPlayed = context.CurrentKeyframe >= StartTime;
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
        foreach (var effect in Effects)
        {
            effect.Dispose();
        }
        foreach (var effect in FinalEffects)
        {
            effect.Dispose();
        }
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
