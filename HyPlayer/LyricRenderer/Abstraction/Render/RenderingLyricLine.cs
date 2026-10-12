#nullable enable
using ALRC.Abstraction;
using HyPlayer.LyricRenderer.Animator;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Numerics;
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

    private GaussianBlurEffect? _blur;
    private ShadowEffect? _glow;
    private Transform3DEffect? _perspective;
    private LyricScalarTransition _opacityTransition;
    private LyricScalarTransition _scaleTransition;
    private LyricScalarTransition _blurTransition;
    private LyricScalarTransition _glowTransition;
    private LyricScalarTransition _angleTransition;

    public virtual bool IsTextLine => false;

    protected virtual float ContentTop => 0;

    internal int LastFrameCommandListCount { get; private set; }

    /// <summary>Draws a line directly; only spatial image effects record its content.</summary>
    public bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        LastFrameCommandListCount = 0;
        var settings = context.Settings.Line;
        var currentY = (uint)context.CurrentLyricLineIndex < (uint)context.RenderOffsets.Count
            ? context.RenderOffsets[context.CurrentLyricLineIndex].Y : offset.Y;
        var distance = IsActive || context.ViewHeight <= 0 ? 0 : Math.Abs((currentY - offset.Y) / context.ViewHeight);
        var time = context.CurrentLyricTime;
        var enabled = settings.Preset != LyricLinePreset.None;
        var opacityTarget = IsActive ? 1 : Math.Clamp(settings.NearOpacity + (settings.FarOpacity - settings.NearOpacity) * distance, settings.FarOpacity, 1);
        if (settings.Preset == LyricLinePreset.Layered && context.IsScrolling) opacityTarget = Math.Max(opacityTarget, 0.4f);
        var opacity = _opacityTransition.Update(enabled ? opacityTarget : 1, time, IsActive ? 0 : 500, context.IsSeek);
        var scaleTarget = enabled ? Math.Max(0.1f, 1 + (settings.DistantScale - 1) * distance) : 1;
        if (context.IsScrolling) scaleTarget = Math.Max(scaleTarget, 0.8f);
        var scale = _scaleTransition.Update(scaleTarget, time, 500, context.IsSeek);
        var blur = _blurTransition.Update(enabled && !context.IsScrolling ? Math.Min(250, settings.MaximumBlur * distance) : 0, time, 500, context.IsSeek);
        var glow = _glowTransition.Update(enabled && IsTextLine && IsActive ? settings.GlowOpacity : 0, time, 500, context.IsSeek);
        var relativeIndex = GroupIndex - (context.CurrentLyricLine?.GroupIndex ?? GroupIndex);
        var angle = _angleTransition.Update(settings.Preset == LyricLinePreset.Layered && !context.IsScrolling && !IsActive
            ? Math.Clamp(-15 * relativeIndex, -60, 60) : 0, time, 1000, context.IsSeek);
        var alignment = TypographySelector(static t => t?.Alignment, context)!.Value;
        var anchor = new Vector2(alignment switch { TextAlignment.Center => RenderingWidth / 2, TextAlignment.Right => RenderingWidth, _ => 0 }, RenderingHeight / 2);
        var originalTransform = session.Transform;
        var transform = Matrix3x2.CreateScale(scale, anchor) * Matrix3x2.CreateTranslation(offset.X, offset.Y) * originalTransform;
        CanvasCommandList? recorded = null;
        try
        {
            session.Transform = transform;
            if (settings.Background)
                session.FillRoundedRectangle(0, 0, RenderingWidth, RenderingHeight, 6, 6, Color.FromArgb(10, 255, 255, 255));

            var hasStatic = TryGetStaticSourceImage(session, context, out var cached);
            var needsImage = blur > 0.001f || glow > 0.001f && settings.GlowRadius > 0 || Math.Abs(angle) > 0.001f;
            bool result;
            if (needsImage)
            {
                ICanvasImage content;
                if (hasStatic) { content = cached; result = true; }
                else
                {
                    recorded = new CanvasCommandList(session);
                    LastFrameCommandListCount = 1;
                    using var lineSession = recorded.CreateDrawingSession();
                    result = RenderCore(lineSession, context);
                    content = recorded;
                }
                if (glow > 0.001f && settings.GlowRadius > 0)
                {
                    _glow ??= new ShadowEffect();
                    _glow.Source = content;
                    _glow.BlurAmount = settings.GlowRadius;
                    _glow.ShadowColor = TypographySelector(static t => t?.FocusingColor, context)!.Value;
                    var glowOutset = settings.GlowRadius * 3;
                    session.DrawImage(_glow, -glowOutset, -glowOutset,
                        new Rect(-glowOutset, -glowOutset, RenderingWidth + 2 * glowOutset, RenderingHeight + 2 * glowOutset), opacity * glow);
                }
                ICanvasImage image = content;
                if (blur > 0.001f)
                {
                    _blur ??= new GaussianBlurEffect { BorderMode = EffectBorderMode.Soft };
                    _blur.Source = image;
                    _blur.BlurAmount = blur;
                    image = _blur;
                }
                if (Math.Abs(angle) > 0.001f)
                {
                    _perspective ??= new Transform3DEffect();
                    var projection = Matrix4x4.Identity;
                    projection.M34 = 1f / 3000;
                    var center = new Vector3(anchor, 0);
                    _perspective.Source = image;
                    _perspective.TransformMatrix = Matrix4x4.CreateTranslation(-center) *
                        Matrix4x4.CreateRotationY(angle * MathF.PI / 180) * projection * Matrix4x4.CreateTranslation(center);
                    image = _perspective;
                }
                // SourceRectangle must include filter outsets; a tight text rectangle clips glow/blur.
                var outset = Math.Max(blur, settings.GlowRadius) * 3;
                session.DrawImage(image, -outset, -outset,
                    new Rect(-outset, -outset, RenderingWidth + 2 * outset, RenderingHeight + 2 * outset), opacity);
            }
            else if (hasStatic)
            {
                session.DrawImage(cached, 0, 0, new Rect(0, 0, RenderingWidth, RenderingHeight), opacity);
                result = true;
            }
            else
            {
                // A layer supplies uniform line opacity without recording a command list.
                using var layer = opacity < 0.9999f ? session.CreateLayer(opacity) : null;
                result = RenderCore(session, context);
            }
            if (settings.HoverBackground && ReactionState == ReactionState.Enter && !Hidden)
                session.FillRoundedRectangle(0, ContentTop, RenderingWidth + 2, RenderingHeight + 8 - ContentTop, 6, 6, Color.FromArgb(10, 255, 255, 255));
            if (settings.ProgressUnderline && IsActive)
            {
                var progress = EndTime <= StartTime ? 1 : Math.Clamp((time - StartTime) / (float)(EndTime - StartTime), 0, 1);
                session.DrawLine(0, RenderingHeight + 4, RenderingWidth * progress, RenderingHeight + 4, Color.FromArgb(204, 255, 255, 255), 2);
            }
            if (context.Debug)
            {
                session.DrawText(RuntimeIndex.ToString(), 0, 15, Colors.Red);
                session.DrawRectangle(0, 0, RenderingWidth, RenderingHeight, Colors.Yellow);
            }
            return result;
        }
        finally
        {
            session.Transform = originalTransform;
            if (_glow is not null) _glow.Source = null;
            if (_blur is not null) _blur.Source = null;
            if (_perspective is not null) _perspective.Source = null;
            recorded?.Dispose();
        }
    }

    protected void ReleaseDrawingResources()
    {
        _blur?.Dispose(); _blur = null;
        _glow?.Dispose(); _glow = null;
        _perspective?.Dispose(); _perspective = null;
        _opacityTransition = default;
        _scaleTransition = default;
        _blurTransition = default;
        _glowTransition = default;
        _angleTransition = default;
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
        if (!rendering) ReleaseDrawingResources();
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
    public virtual void Dispose() => ReleaseDrawingResources();

    public T TypographySelector<T>(Func<RenderTypography?, T?> selector, RenderContext context)
    {
        return (selector(Typography) ??
                (SourceStyle?.Type == ALRCStyleAccent.Background
                    ? selector(context.SublineTypography)
                    : default) ??
                selector(context.PreferTypography) ?? selector(RenderTypography.Default))!;
    }
}
public enum ReactionState
{
    Leave,
    Enter,
    Press
}
