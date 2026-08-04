#nullable enable
using ALRC.Abstraction;
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
    public int RuntimeIndex { get; set; }

    public int FactoIndex { get; set; }

    public int GroupIndex { get; set; }

    public long GroupStartTime { get; set; }

    public long GroupEndTime { get; set; }

    public ALRCLine? SourceLine { get; set; }

    public ALRCStyle? SourceStyle { get; set; }

    public IReadOnlyDictionary<string, ALRCStyle> StyleTable { get; set; } =
        new Dictionary<string, ALRCStyle>(StringComparer.Ordinal);

    public Color? SourceStyleColor { get; set; }

    public RenderTypography? Typography { get; set; }

    public float RenderingHeight { get; set; }

    public float RenderingWidth { get; set; }

    private bool _rendering;

    public bool Rendering
    {
        get => _rendering;
        set
        {
            if (_rendering == value) return;
            _rendering = value;
            OnRenderingChanged(value);
        }
    }

    public ReactionState ReactionState { get; set; }

    public bool Hidden { get; set; }

    public bool HiddenOnBlur { get; set; }

    public List<long>? KeyFrames { get; set; }

    public long StartTime { get; set; }

    public long EndTime { get; set; }

    public bool IsActive { get; private set; }

    public bool IsStarted { get; private set; }

    public bool IsFinished { get; private set; }

    private LyricRenderPipelineInstance? _renderPipeline;
    private readonly LyricRenderFrameResourceScope _frameResources = new();
    private readonly LyricRenderOperationContext _operationContext = new();
    private ALRCStyle? _cachedExpressionStyle;
    private string _cachedStylePosition = "Undefined";
    private string _cachedStyleType = "Normal";

    protected LyricExpressionLine CurrentExpressionLine { get; private set; }

    protected LyricExpressionFrame CurrentExpressionFrame { get; private set; }

    public virtual string ExpressionText => string.Empty;

    public virtual bool IsTextLine => false;

    public bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        CurrentExpressionLine = CreateExpressionLine(context, offset);
        CurrentExpressionFrame = CreateExpressionFrame(context);
        CanvasCommandList? transientSource = null;
        ICanvasImage sourceImage;
        bool result;
        if (TryGetStaticSourceImage(session, context, out var staticSource))
        {
            sourceImage = staticSource;
            result = true;
        }
        else
        {
            transientSource = new CanvasCommandList(session);
            sourceImage = transientSource;
            using var currentLineSession = transientSource.CreateDrawingSession();
            result = RenderCore(currentLineSession, context);
        }

        try
        {
            ICanvasImage finalImage;
            if (context.EffectProfile is { } profile)
            {
                if (_renderPipeline?.Version != profile.Version)
                {
                    _renderPipeline?.Dispose();
                    _renderPipeline = profile.CreatePipeline();
                }

                if (context.EmptyPipelineImage is null)
                {
                    context.EmptyPipelineImage = new CanvasCommandList(session);
                    using var emptySession = context.EmptyPipelineImage.CreateDrawingSession();
                }

                _operationContext.SourceImage = sourceImage;
                _operationContext.TargetSession = session;
                _operationContext.Resources = _frameResources;
                _operationContext.Line = CurrentExpressionLine;
                _operationContext.Frame = CurrentExpressionFrame;
                _operationContext.OffsetX = offset.X;
                _operationContext.OffsetY = offset.Y;
                _operationContext.DebugEnabled = context.Debug;
                _operationContext.GeometryBounds = new Windows.Foundation.Rect(0, 0, RenderingWidth, RenderingHeight);
                _operationContext.HasContent = false;
                try
                {
                    finalImage = _renderPipeline!.Apply(context.EmptyPipelineImage, _operationContext);
                    session.DrawImage(finalImage, offset.X, offset.Y);
                }
                finally
                {
                    _frameResources.Dispose();
                }
            }
            else
            {
                session.DrawImage(sourceImage, offset.X, offset.Y);
                // 配置服务尚未初始化时保留调试回退绘制。
                if (!context.Debug) return result;
                session.DrawText($"(X{offset.X},Y{offset.Y},W{RenderingWidth},H{RenderingHeight})", offset.X, offset.Y, Colors.Red);
                session.DrawText(RuntimeIndex.ToString(), offset.X, offset.Y + 15, Colors.Red);
                session.DrawRectangle(offset.X, offset.Y, RenderingWidth, RenderingHeight, Colors.Yellow);
            }

            return result;
        }
        finally
        {
            transientSource?.Dispose();
        }
    }



    public void GoToReactionState(ReactionState state, RenderContext context)
    {
        ReactionState = state;
    }
    protected abstract bool RenderCore(CanvasDrawingSession session, RenderContext context);

    protected virtual bool TryGetStaticSourceImage(
        CanvasDrawingSession session,
        RenderContext context,
        out ICanvasImage image)
    {
        image = null!;
        return false;
    }

    protected virtual void OnRenderingChanged(bool rendering)
    {
    }

    public void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        IsActive = context.CurrentKeyframe >= StartTime && context.CurrentKeyframe < EndTime;
        IsStarted = context.CurrentKeyframe >= StartTime;
        IsFinished = context.CurrentKeyframe >= EndTime;
        Hidden = HiddenOnBlur && !IsActive;
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
        _frameResources.Dispose();
    }

    protected LyricExpressionLine CreateExpressionLine(RenderContext context, LineRenderOffset offset)
    {
        var currentGroupIndex = context.CurrentLyricLine?.GroupIndex ?? GroupIndex;
        var relativeIndex = GroupIndex - currentGroupIndex;
        var currentFactoIndex = context.CurrentLyricLine?.FactoIndex ?? FactoIndex;
        var factoRelativeIndex = FactoIndex - currentFactoIndex;
        var currentOffsetY = context.RenderOffsets.TryGetValue(context.CurrentLyricLineIndex, out var currentOffset)
            ? currentOffset.Y
            : offset.Y;
        var viewportDistance = context.ViewHeight <= 0
            ? 0
            : Math.Abs((currentOffsetY - offset.Y) / context.ViewHeight);
        if (IsActive) viewportDistance = 0;

        var duration = EndTime - StartTime;
        var progress = duration <= 0
            ? (IsStarted ? 1f : 0f)
            : Math.Clamp((context.CurrentLyricTime - StartTime) / (float)duration, 0, 1);
        var alignment = TypographySelector(t => t?.Alignment, context)!.Value;
        var anchorX = alignment switch
        {
            TextAlignment.Center => RenderingWidth / 2,
            TextAlignment.Right => RenderingWidth,
            _ => 0
        };
        var idle = TypographySelector(t => t?.IdleColor, context)!.Value;
        var focusing = TypographySelector(t => t?.FocusingColor, context)!.Value;
        var source = SourceLine;
        var style = SourceStyle;
        if (!ReferenceEquals(style, _cachedExpressionStyle))
        {
            _cachedExpressionStyle = style;
            _cachedStylePosition = style?.Position?.ToString() ?? "Undefined";
            _cachedStyleType = style?.Type?.ToString() ?? "Normal";
        }
        var styleColor = SourceStyleColor ?? default;

        return new LyricExpressionLine(
            GroupIndex,
            relativeIndex,
            Math.Abs(relativeIndex),
            new LyricExpressionLineFacto(FactoIndex, factoRelativeIndex, Math.Abs(factoRelativeIndex)),
            viewportDistance,
            IsActive,
            IsStarted,
            IsFinished,
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
            ToExpressionColor(focusing),
            source?.Id ?? string.Empty,
            source?.ParentLineId ?? string.Empty,
            source?.LineStyle ?? string.Empty,
            source?.Comment ?? string.Empty,
            source?.RawText ?? string.Empty,
            source?.Transliteration ?? string.Empty,
            source?.Translation ?? string.Empty,
            new LyricExpressionLineStyle(
                style is not null,
                _cachedStylePosition,
                SourceStyleColor.HasValue,
                ToExpressionColor(styleColor),
                _cachedStyleType,
                style?.HiddenOnBlur == true));
    }

    protected static LyricExpressionFrame CreateExpressionFrame(RenderContext context) =>
        new(
            context.CurrentLyricLine?.GroupIndex ?? 0,
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
                (SourceStyle?.Type == ALRCStyleAccent.Background
                    ? expression(context.SublineTypography)
                    : default) ??
                expression(context.PreferTypography) ?? expression(RenderTypography.Default))!;
    }
}
public enum ReactionState
{
    Leave,
    Enter,
    Press
}
