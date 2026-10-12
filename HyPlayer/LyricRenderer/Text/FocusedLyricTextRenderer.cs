#nullable enable
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

/// <summary>Draws cached glyph runs directly with shared brushes and numeric motion.</summary>
public sealed class FocusedLyricTextRenderer : IDisposable
{
    private CanvasSolidColorBrush? _solid;
    private CanvasLinearGradientBrush? _highlight;
    private CanvasLinearGradientBrush? _pending;
    private LyricTextLayoutSnapshot? _brushLayout;
    private Color _solidColor;
    private float _rampStart = float.NaN;
    private float _rampEnd = float.NaN;

    /// <summary>Draws all three text layers. Translation remains independent of the scan.</summary>
    public void Render(CanvasDrawingSession session, LyricTextLayoutSnapshot layout, RenderContext context)
    {
        EnsureBrushes(session, layout);
        session.DrawImage(layout.StaticPersistCache, 0, 0, layout.SizePixelRect, 1);
        var settings = context.Settings.Focused;
        DrawLayer(session, layout, layout.LyricGlyphClusters, layout.LyricVisualLines, context.CurrentLyricTime,
            settings, false, context.Effects.TransliterationScanning);
        if (context.EnableTransliteration)
            DrawLayer(session, layout, layout.TransliterationGlyphClusters, layout.TransliterationVisualLines,
                context.CurrentLyricTime, settings, true, context.Effects.TransliterationScanning);
        if (context.EnableTranslation && layout.DefaultTranslationPersistCache is not null)
            session.DrawImage(layout.DefaultTranslationPersistCache, 0, layout.TranslationRenderActualTop, layout.SizePixelRect, 1);
    }

    private void EnsureBrushes(CanvasDrawingSession session, LyricTextLayoutSnapshot layout)
    {
        if (ReferenceEquals(layout, _brushLayout)) return;
        Dispose();
        _solid = new CanvasSolidColorBrush(session, layout.IdleColor);
        _solidColor = layout.IdleColor;
        _highlight = CreateGradient(session, layout.FocusingColor, true);
        _pending = CreateGradient(session, layout.IdleColor, false);
        _brushLayout = layout;
    }

    private static CanvasLinearGradientBrush CreateGradient(CanvasDrawingSession session, Color color, bool highlighted)
    {
        // Clamp supplies the old five-stop mask's two constant outer regions.
        Color At(float alpha) => Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);
        return new CanvasLinearGradientBrush(session,
        [
            new CanvasGradientStop { Position = 0, Color = At(highlighted ? 1 : 0) },
            new CanvasGradientStop { Position = 0.5f, Color = At(highlighted ? 0.0625f : 0.9375f) },
            new CanvasGradientStop { Position = 1, Color = At(highlighted ? 0 : 1) }
        ]);
    }

    private void DrawLayer(CanvasDrawingSession session, LyricTextLayoutSnapshot layout,
        IReadOnlyList<LyricGlyphCluster> clusters, DirectLyricVisualLine[] lines, long time,
        FocusedTextSettings settings, bool transliteration, bool scanTransliteration)
    {
        var useWords = layout.HasRealWords || settings.UntimedHighlight == UntimedHighlightMode.InferWords;
        var wholeLine = transliteration && (settings.Transliteration == TransliterationProgressMode.WholeLine || !scanTransliteration);
        foreach (var line in lines)
        {
            var timeline = line.Timeline;
            var sampleTime = AdjustLineRevealTime(time, timeline.StartTimeMs, timeline.EndTimeMs, settings.RevealTimeOffsetMs);
            var sample = timeline.Sample(sampleTime);
            for (var index = line.Start; index < line.End; index++)
            {
                var cluster = clusters[index];
                var tokenIndex = cluster.TokenStartIndex;
                var token = (uint)tokenIndex < (uint)layout.Tokens.Count ? layout.Tokens[tokenIndex] : null;
                var lift = transliteration ? 0 : CalculateLift(cluster, token, layout, time, settings);
                var origin = cluster.BaseState.Origin - new Vector2(0, lift);
                if (wholeLine || !useWords || token is null)
                {
                    DrawSolid(session, cluster, origin, wholeLine || settings.UntimedHighlight == UntimedHighlightMode.WholeLine
                        ? layout.FocusingColor : layout.IdleColor);
                    continue;
                }
                if (settings.RevealMode != HighlightRevealMode.RectangleClip)
                {
                    var progress = GetRevealWordProgress(time, token.StartTime, token.EndTime, settings.RevealTimeOffsetMs);
                    progress = FocusedTextProgress.GetRevealProgress(settings.RevealMode, progress,
                        cluster.TokenClusterIndex, cluster.TokenClusterCount);
                    SetSolidColor(layout.IdleColor);
                    _solid!.Opacity = 1 - progress;
                    DrawGlyphRun(session, cluster.BaseState, origin, _solid);
                    SetSolidColor(layout.FocusingColor);
                    _solid.Opacity = progress;
                    DrawGlyphRun(session, cluster.BaseState, origin, _solid);
                    _solid.Opacity = 1;
                    continue;
                }
                var left = timeline.Left;
                var right = timeline.Right;
                var boundary = sample.Position;
                var rtl = timeline.IsRightToLeft;
                var progressOnLine = sample.Progress;
                if (!timeline.IsSpatiallyMonotone)
                {
                    var span = timeline.Spans[line.SpanIndexes[index - line.Start]];
                    var spanSample = timeline.SampleSpan(span, sampleTime);
                    boundary = spanSample.Position;
                    progressOnLine = spanSample.Progress;
                    rtl = span.Trailing < span.Leading;
                    left = Math.Min(span.Leading, span.Trailing);
                    right = Math.Max(span.Leading, span.Trailing);
                }
                DrawReveal(session, cluster, origin, layout, boundary, progressOnLine,
                    rtl, left, right, settings.FeatherDip);
            }
        }
    }

    private static float CalculateLift(LyricGlyphCluster cluster, LyricTextToken? token,
        LyricTextLayoutSnapshot layout, long time, FocusedTextSettings settings)
    {
        if (!settings.LiftEnabled || settings.LiftHeight == 0) return 0;
        if (!layout.HasRealWords && settings.UntimedLift == UntimedLiftMode.DoNotLift) return 0;
        var asLine = !layout.HasRealWords && settings.UntimedLift == UntimedLiftMode.WholeLine;
        if (!asLine && token is null) return 0;
        var start = asLine ? layout.StartTimeMs : token!.StartTime;
        var end = asLine ? layout.EndTimeMs : token!.EndTime;
        var raw = FocusedTextProgress.GetTimedProgress(time, start, end, settings.LiftTimeOffsetMs,
            settings.LiftFinishDurationMs, settings.LiftMotion);
        var progress = FocusedTextProgress.GetLiftProgress(raw, end - start, cluster.TokenClusterIndex,
            cluster.TokenClusterCount, settings.LiftOverlap, settings.WholeWordThresholdMs,
            asLine ? GlyphLiftUnit.Word : settings.LiftUnit, settings.LiftMotion);
        return settings.LiftHeight * progress;
    }

    private void DrawReveal(CanvasDrawingSession session, LyricGlyphCluster cluster, Vector2 origin,
        LyricTextLayoutSnapshot layout, float boundary, float progress, bool rtl, float left, float right, float feather)
    {
        if (progress <= 0) { DrawSolid(session, cluster, origin, layout.IdleColor); return; }
        if (progress >= 1) { DrawSolid(session, cluster, origin, layout.FocusingColor); return; }
        var rampEnd = rtl ? Math.Max(left, boundary - feather) : Math.Min(right, boundary + feather);
        var rampLeft = Math.Min(boundary, rampEnd);
        var rampRight = Math.Max(boundary, rampEnd);
        if (rtl ? cluster.VisualLeft >= rampRight : cluster.VisualRight <= rampLeft)
        { DrawSolid(session, cluster, origin, layout.FocusingColor); return; }
        if (rtl ? cluster.VisualRight <= rampLeft : cluster.VisualLeft >= rampRight)
        { DrawSolid(session, cluster, origin, layout.IdleColor); return; }
        if (feather <= 0 || Math.Abs(rampEnd - boundary) < 0.001f)
        {
            DrawClipped(session, cluster, origin, layout.IdleColor, boundary, !rtl, layout.RenderingHeight);
            DrawClipped(session, cluster, origin, layout.FocusingColor, boundary, rtl, layout.RenderingHeight);
            return;
        }
        if (_rampStart != boundary || _rampEnd != rampEnd)
        {
            _highlight!.StartPoint = _pending!.StartPoint = new(boundary, 0);
            _highlight.EndPoint = _pending.EndPoint = new(rampEnd, 0);
            _rampStart = boundary;
            _rampEnd = rampEnd;
        }
        DrawGlyphRun(session, cluster.BaseState, origin, _pending!);
        DrawGlyphRun(session, cluster.BaseState, origin, _highlight!);
    }

    private void DrawSolid(CanvasDrawingSession session, LyricGlyphCluster cluster, Vector2 origin, Color color)
    {
        SetSolidColor(color);
        DrawGlyphRun(session, cluster.BaseState, origin, _solid!);
    }

    private void SetSolidColor(Color color)
    {
        if (_solidColor == color) return;
        _solid!.Color = color;
        _solidColor = color;
    }

    private void DrawClipped(CanvasDrawingSession session, LyricGlyphCluster cluster, Vector2 origin,
        Color color, float boundary, bool rtl, float height)
    {
        var clip = new Rect(rtl ? boundary : -10000, -height, rtl ? 10000 : Math.Max(0, boundary + 10000), height * 3);
        using var layer = session.CreateLayer(1, clip);
        DrawSolid(session, cluster, origin, color);
    }

    internal static void DrawGlyphRun(CanvasDrawingSession session, LyricGlyphDrawState state, Vector2 origin, ICanvasBrush brush) =>
        session.DrawGlyphRun(origin, state.FontFace, state.FontSize, state.Glyphs, state.IsSideways,
            state.BidiLevel, brush, state.MeasuringMode, state.LocaleName, state.TextString, state.ClusterMap, state.CharacterIndex);

    private static float GetRevealWordProgress(long time, long start, long end, float offset)
    {
        var adjustedStart = offset < 0 ? start - offset : start;
        var adjustedEnd = offset > 0 ? end - offset : end;
        return adjustedEnd <= adjustedStart ? time >= adjustedEnd ? 1 : 0 : Math.Clamp((time - adjustedStart) / (adjustedEnd - adjustedStart), 0, 1);
    }

    /// <summary>Releases device brushes; the next draw creates them for the current layout.</summary>
    public void Dispose()
    {
        _solid?.Dispose(); _solid = null;
        _highlight?.Dispose(); _highlight = null;
        _pending?.Dispose(); _pending = null;
        _brushLayout = null;
        _rampStart = _rampEnd = float.NaN;
    }
    internal static FocusedRevealClip? GetLineVectorContributionClip(
        float clusterLeft,
        float clusterRight,
        float boundary,
        bool highlighted,
        bool isRightToLeft,
        float renderingHeight)
    {
        clusterRight = Math.Max(clusterRight, clusterLeft);
        var visibleLeft = highlighted == isRightToLeft ? boundary : clusterLeft;
        var visibleRight = highlighted == isRightToLeft ? clusterRight : boundary;
        visibleLeft = Math.Clamp(visibleLeft, clusterLeft, clusterRight);
        visibleRight = Math.Clamp(visibleRight, clusterLeft, clusterRight);
        if (visibleLeft <= clusterLeft && visibleRight >= clusterRight) return null;
        var height = Math.Max(renderingHeight, 1);
        return new FocusedRevealClip(visibleLeft, -height, Math.Max(visibleRight - visibleLeft, 0), height * 3);
    }

    internal static RectangleMaskPlan CreateRectangleMaskPlan(
        float reveal,
        bool highlighted,
        bool isRightToLeft,
        float feather,
        float width)
    {
        if (highlighted && reveal >= 1 || !highlighted && reveal <= 0)
            return new RectangleMaskPlan(1, 0, 0, 0, 1, 1, 1, 1, 1);
        if (highlighted && reveal <= 0 || !highlighted && reveal >= 1)
            return new RectangleMaskPlan(0, 0, 0, 0, 0, 0, 0, 0, 0);

        var boundaryProgress = Math.Clamp(reveal, 0, 1);
        var normalizedFeather = Math.Max(feather, 0) / Math.Max(width, 0.001f);
        // The timeline position is the fully highlighted edge. Feathering leads that
        // edge and may spill into the next token; it must never pull the solid edge
        // back inside the token that has just completed.
        var rampStart = boundaryProgress;
        // Contract the feather at the line end. Raising the opacity of the
        // entire remaining line would light its final glyphs before the scan.
        var rampEnd = Math.Min(1, boundaryProgress + normalizedFeather);
        var rampMiddle = (rampStart + rampEnd) / 2;
        var firstStop = isRightToLeft
            ? 1 - Math.Clamp(rampEnd, 0, 1)
            : Math.Clamp(rampStart, 0, 1);
        var middleStop = isRightToLeft
            ? 1 - Math.Clamp(rampMiddle, 0, 1)
            : Math.Clamp(rampMiddle, 0, 1);
        var secondStop = isRightToLeft
            ? 1 - Math.Clamp(rampStart, 0, 1)
            : Math.Clamp(rampEnd, 0, 1);

        float OpacityAt(float position)
        {
            var scanPosition = isRightToLeft ? 1 - position : position;
            var featherOpacity = scanPosition <= rampStart
                ? 1
                : scanPosition >= rampEnd
                    ? 0
                    : MathF.Pow(
                        (rampEnd - scanPosition) / Math.Max(rampEnd - rampStart, float.Epsilon),
                        4f);
            return highlighted ? featherOpacity : 1 - featherOpacity;
        }

        return new RectangleMaskPlan(
            null,
            firstStop,
            middleStop,
            secondStop,
            OpacityAt(0),
            OpacityAt(firstStop),
            OpacityAt(middleStop),
            OpacityAt(secondStop),
            OpacityAt(1));
    }

    private static long AdjustLineRevealTime(long timeMs, long startMs, long endMs, float offset)
    {
        if (endMs <= startMs) return timeMs;
        var adjustedStart = offset < 0 ? startMs - offset : startMs;
        var adjustedEnd = offset > 0 ? endMs - offset : endMs;
        if (adjustedEnd <= adjustedStart) return timeMs >= adjustedEnd ? endMs : startMs;
        var progress = Math.Clamp((timeMs - adjustedStart) / (float)(adjustedEnd - adjustedStart), 0, 1);
        return startMs + (long)Math.Round((endMs - startMs) * progress);
    }
    internal readonly record struct RectangleMaskPlan(float? ConstantOpacity, float FirstStop, float MiddleStop, float SecondStop, float StartOpacity, float FirstOpacity, float MiddleOpacity, float SecondOpacity, float EndOpacity);
}
