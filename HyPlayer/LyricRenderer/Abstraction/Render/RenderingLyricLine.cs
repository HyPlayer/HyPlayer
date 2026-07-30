#nullable enable
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricRenderer.Pipeline;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;

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

    private LyricRenderPipelineInstance? _renderPipeline;

    public virtual string ExpressionText => string.Empty;

    public virtual bool IsTextLine => false;

    public bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        using var commandList = new CanvasCommandList(session);
        bool result;
        using (var currentLineSession = commandList.CreateDrawingSession())
        {
            result = RenderCore(currentLineSession, context);
        }

        ICanvasImage finalImage;
        if (context.EffectProfile is { } profile)
        {
            if (_renderPipeline?.Version != profile.Version)
            {
                _renderPipeline?.Dispose();
                _renderPipeline = profile.CreatePipeline();
            }

            using var emptyImage = new CanvasCommandList(session);
            using (emptyImage.CreateDrawingSession())
            {
            }

            using var resources = new LyricRenderFrameResourceScope();
            finalImage = _renderPipeline!.Apply(emptyImage, new LyricRenderOperationContext
            {
                SourceImage = commandList,
                TargetSession = session,
                Resources = resources,
                Line = CreateExpressionLine(context, offset),
                Frame = CreateExpressionFrame(context),
                OffsetX = offset.X,
                OffsetY = offset.Y,
                DebugEnabled = context.Debug
            });
            session.DrawImage(finalImage, offset.X, offset.Y);
        }
        else
        {
            session.DrawImage(commandList, offset.X, offset.Y);
            // 配置服务尚未初始化时保留调试回退绘制。
            if (!context.Debug) return result;
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
        _renderPipeline?.Dispose();
        _renderPipeline = null;
    }

    private LyricExpressionLine CreateExpressionLine(RenderContext context, LineRenderOffset offset)
    {
        var relativeIndex = Id - context.CurrentLyricLineIndex;
        var currentOffsetY = context.RenderOffsets.TryGetValue(context.CurrentLyricLineIndex, out var currentOffset)
            ? currentOffset.Y
            : offset.Y;
        var viewportDistance = context.ViewHeight <= 0
            ? 0
            : Math.Abs((currentOffsetY - offset.Y) / context.ViewHeight);
        if (IsActive) viewportDistance = 0;

        var duration = EndTime - StartTime;
        var progress = duration <= 0
            ? (IsPlayed ? 1f : 0f)
            : Math.Clamp((context.CurrentLyricTime - StartTime) / (float)duration, 0, 1);
        var alignment = TypographySelector(t => t?.Alignment, context)!.Value;
        var anchorX = alignment switch
        {
            TextAlignment.Center => RenderingWidth / 2,
            TextAlignment.Right => RenderingWidth,
            _ => 0
        };
        var idle = TypographySelector(t => t?.IdleColor, context)!.Value;
        var accent = TypographySelector(t => t?.FocusingColor, context)!.Value;

        return new LyricExpressionLine(
            Id,
            relativeIndex,
            Math.Abs(relativeIndex),
            viewportDistance,
            IsActive,
            IsPlayed,
            ReactionState == ReactionState.Enter,
            Hidden,
            IsTextLine,
            StartTime,
            EndTime,
            progress,
            RenderingWidth,
            RenderingHeight,
            anchorX,
            RenderingHeight / 2,
            ExpressionText,
            ToExpressionColor(idle),
            ToExpressionColor(accent));
    }

    private static LyricExpressionFrame CreateExpressionFrame(RenderContext context) =>
        new(
            context.CurrentLyricLineIndex,
            context.CurrentLyricTime,
            context.RenderTick / TimeSpan.TicksPerMillisecond,
            context.IsPlaying,
            context.IsScrolling,
            context.IsSeek,
            context.ScrollingDelta,
            context.ViewWidth,
            context.ViewHeight,
            context.Dpi,
            context.BeatPerMinute);

    private static LyricColorValue ToExpressionColor(Color color) =>
        new(color.A, color.R, color.G, color.B);

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
