#nullable enable

using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Pipeline;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public sealed class FocusedLyricTextRenderer
{
    private static readonly ConcurrentDictionary<string, byte> ReportedOperationFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<FocusedTransitionKey, ScalarTransitionState> _scalarTransitions = [];
    private readonly Dictionary<FocusedTransitionKey, ColorTransitionState> _colorTransitions = [];
    private readonly Dictionary<LyricGlyphCluster, CanvasCommandList> _glyphSources = [];
    private readonly Dictionary<LineRevealKey, FocusedLineRevealTimeline> _lineRevealTimelines = [];
    private readonly Dictionary<LineRevealFrameKey, LineRevealFrame> _lineRevealFrames = [];
    private readonly Dictionary<LineMaskKey, CanvasCommandList> _lineMasks = [];
    private readonly FocusedTextExpressionFrameCache _expressionFrameCache = new();
    private readonly LyricRenderFrameResourceScope _contributionResources = new();
    private readonly LyricRenderFrameResourceScope _lineRevealResources = new();
    private LyricTextLayoutSnapshot? _transitionLayout;
    private CompiledFocusedTextEffectProfile? _transitionProfile;

    public void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame)
    {
        _expressionFrameCache.Clear();
        _lineRevealResources.Dispose();
        _lineRevealFrames.Clear();
        _lineMasks.Clear();
        // A renderer is kept by a line for its entire lifetime.  Layouts and profiles are
        // replaced during resize/preview, so transition keys from the old generation must
        // not remain reachable indefinitely.
        if (!ReferenceEquals(_transitionLayout, layout) || !ReferenceEquals(_transitionProfile, profile))
        {
            _scalarTransitions.Clear();
            _colorTransitions.Clear();
            _lineRevealTimelines.Clear();
            ReleaseRasterCache();
            _transitionLayout = layout;
            _transitionProfile = profile;
        }

        CompiledFocusedTextOperation? reveal = null;
        for (var index = 0; index < profile.Operations.Count; index++)
        {
            if (profile.Operations[index].Definition.TypeId != FocusedTextBuiltInOperationTypes.HighlightReveal)
                continue;
            reveal = profile.Operations[index];
            break;
        }
        var revealOptions = RevealOptions.From(reveal?.Definition);
        var vectorPath = CanUseVectorPath(profile, reveal);
        using var brush = new CanvasSolidColorBrush(session, layout.FocusingColor);
        DrawLayer(session, brush, layout.LyricGlyphClusters, LyricTextLayer.Lyric, layout, renderContext,
            profile, revealOptions, line, expressionFrame, vectorPath);
        if (renderContext.EnableTransliteration)
            DrawLayer(session, brush, layout.TransliterationGlyphClusters, LyricTextLayer.Transliteration, layout,
                renderContext, profile, revealOptions, line, expressionFrame, vectorPath);
        if (renderContext.EnableTranslation)
            DrawLayer(session, brush, layout.TranslationGlyphClusters, LyricTextLayer.Translation, layout,
                renderContext, profile, revealOptions, line, expressionFrame, vectorPath);
    }

    private static bool CanUseVectorPath(
        CompiledFocusedTextEffectProfile profile,
        CompiledFocusedTextOperation? reveal)
    {
        // This is the hot path used by the built-in profile (Opacity -> Reveal -> Lift).
        // It is deliberately conservative: anything that needs an intermediate image keeps
        // the full ordered Win2D pipeline and therefore retains its exact semantics.
        if (reveal is null)
            return false;
        if (reveal.Definition.Parameters.TryGetValue("featherDip", out var feather) &&
            (feather.Transition is not null ||
             !float.TryParse(feather.Expression, System.Globalization.NumberStyles.Float,
                 System.Globalization.CultureInfo.InvariantCulture, out var featherValue) ||
             !float.IsFinite(featherValue) || featherValue > 0))
            return false;

        for (var index = 0; index < profile.Operations.Count; index++)
        {
            var operation = profile.Operations[index];
            if (operation.DrawScript is not null) return false;
            var type = operation.Definition.TypeId;
            if (type is not (FocusedTextBuiltInOperationTypes.HighlightReveal or
                FocusedTextBuiltInOperationTypes.Color or
                FocusedTextBuiltInOperationTypes.Opacity or
                FocusedTextBuiltInOperationTypes.GlyphLift))
                return false;
        }

        return true;
    }

    private void DrawLayer(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        IReadOnlyList<LyricGlyphCluster> clusters,
        LyricTextLayer layer,
        LyricTextLayoutSnapshot layout,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        RevealOptions revealOptions,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        bool vectorPath)
    {
        for (var clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
        {
            var cluster = clusters[clusterIndex];
            var contributions = CreateContributions(cluster, layer, layout, renderContext.CurrentLyricTime,
                revealOptions, line);
            if (ShouldDrawContribution(contributions.First, layout, profile, revealOptions, line, frame))
                DrawPlannedContribution(session, brush, contributions.First, layout, renderContext, profile, line,
                    frame, vectorPath);
            if (contributions.Second is { } second &&
                ShouldDrawContribution(second, layout, profile, revealOptions, line, frame))
                DrawPlannedContribution(session, brush, second, layout, renderContext, profile, line, frame,
                    vectorPath);
        }
    }

    private bool ShouldDrawContribution(
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        CompiledFocusedTextEffectProfile profile,
        RevealOptions options,
        LyricExpressionLine line,
        LyricExpressionFrame frame)
    {
        CompiledFocusedTextOperation? reveal = null;
        for (var index = 0; index < profile.Operations.Count; index++)
        {
            if (profile.Operations[index].Definition.TypeId != FocusedTextBuiltInOperationTypes.HighlightReveal)
                continue;
            reveal = profile.Operations[index];
            break;
        }
        if (reveal is null) return true;
        if (!ShouldUseLineRevealForTarget(options.Mode, reveal.Targets, contribution.Target) ||
            !CanPruneLineContributions(profile)) return true;

        var lineFrame = GetLineRevealFrame(reveal, contribution, layout, line, frame);
        var timeline = lineFrame.Timeline;
        if (!timeline.IsSpatiallyMonotone) return true;
        var start = timeline.IsRightToLeft
            ? (timeline.Right - contribution.Cluster.VisualRight) / timeline.Width
            : (contribution.Cluster.VisualLeft - timeline.Left) / timeline.Width;
        var end = timeline.IsRightToLeft
            ? (timeline.Right - contribution.Cluster.VisualLeft) / timeline.Width
            : (contribution.Cluster.VisualRight - timeline.Left) / timeline.Width;
        start = Math.Clamp(start, 0, 1);
        end = Math.Clamp(end, start, 1);
        var normalizedFeather = lineFrame.Feather / timeline.Width;
        var rampStart = lineFrame.Sample.Progress;
        var rampEnd = rampStart + normalizedFeather;
        return IsHighlightedContribution(contribution.State)
            ? start < rampEnd || lineFrame.Sample.Progress >= 1
            : end > rampStart || lineFrame.Sample.Progress <= 0;
    }

    private static bool CanPruneLineContributions(CompiledFocusedTextEffectProfile profile)
    {
        for (var index = 0; index < profile.Operations.Count; index++)
        {
            var operation = profile.Operations[index];
            if (operation.DrawScript is not null) return false;
            if (operation.Definition.TypeId is not (FocusedTextBuiltInOperationTypes.HighlightReveal or
                FocusedTextBuiltInOperationTypes.Color or
                FocusedTextBuiltInOperationTypes.Opacity or
                FocusedTextBuiltInOperationTypes.GlyphLift))
                return false;
        }
        return true;
    }

    private void DrawPlannedContribution(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        bool vectorPath)
    {
        if (vectorPath)
            DrawVectorContribution(session, brush, contribution, layout, renderContext, profile, line, frame);
        else
            DrawContribution(session, contribution, layout, renderContext, profile, line, frame);
    }

    private void DrawVectorContribution(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame frame)
    {
        var state = LyricGlyphDrawState.FromCluster(contribution.Cluster, layout.FocusingColor);
        var revealProgress = contribution.WordProgress;
        var hasRectangleClip = false;
        var highlighted = false;
        LineRevealFrame? lineRevealFrame = null;
        var currentOrigin = state.Origin;

        for (var operationIndex = 0; operationIndex < profile.Operations.Count; operationIndex++)
        {
            var operation = profile.Operations[operationIndex];
            if (!ShouldApplyOperationToTarget(operation.Definition.TypeId, operation.Targets,
                    contribution.Target)) continue;
            var scopes = CreateScopes(contribution, line, revealProgress, 0);
            var inputState = state;
            var inputOrigin = currentOrigin;
            var inputRevealProgress = revealProgress;
            var inputHasRectangleClip = hasRectangleClip;
            var inputHighlighted = highlighted;
            try
            {
                switch (operation.Definition.TypeId)
                {
                    case FocusedTextBuiltInOperationTypes.HighlightReveal:
                        revealProgress = contribution.WordProgress;
                        var options = RevealOptions.From(operation.Definition);
                        if (options.Mode == HighlightRevealMode.RectangleClip ||
                            contribution.State is FocusedTargetState.CurrentHighlighted or FocusedTargetState.CurrentPending)
                        {
                            var offset = Scalar(operation, contribution, "revealTimeOffsetMs", line, frame, scopes, 0);
                            var adjusted = GetRevealWordProgress(frame.CurrentTimeMs, contribution.WordStart,
                                contribution.WordEnd, offset);
                            revealProgress = FocusedTextProgress.GetRevealProgress(options.Mode, adjusted,
                                contribution.Cluster.TokenClusterIndex, contribution.Cluster.TokenClusterCount);
                            highlighted = IsHighlightedContribution(contribution.State);
                            if (options.Mode == HighlightRevealMode.RectangleClip)
                            {
                                hasRectangleClip = true;
                                lineRevealFrame = GetLineRevealFrame(operation, contribution, layout, line, frame);
                            }
                            else
                            {
                                state.Opacity *= highlighted ? revealProgress : 1 - revealProgress;
                            }
                        }
                        break;
                    case FocusedTextBuiltInOperationTypes.Color:
                        var color = ColorValue(operation, contribution, "color", line, frame, scopes,
                            new LyricColorValue(layout.FocusingColor.A, layout.FocusingColor.R,
                                layout.FocusingColor.G, layout.FocusingColor.B));
                        // AlphaMaskEffect multiplies the replacement color by the complete
                        // input alpha. Preserve that accumulation when drawing directly.
                        state.Opacity *= state.Color.A / 255f;
                        state.Color = Color.FromArgb(color.A, color.R, color.G, color.B);
                        break;
                    case FocusedTextBuiltInOperationTypes.Opacity:
                        state.Opacity *= Scalar(operation, contribution, "opacity", line, frame, scopes, 1);
                        break;
                    case FocusedTextBuiltInOperationTypes.GlyphLift:
                        ApplyLiftVector(operation, contribution, layout, renderContext.CurrentLyricTime,
                            line, frame, revealProgress, ref state, ref currentOrigin);
                        break;
                }
            }
            catch (Exception exception)
            {
                state = inputState;
                currentOrigin = inputOrigin;
                revealProgress = inputRevealProgress;
                hasRectangleClip = inputHasRectangleClip;
                highlighted = inputHighlighted;
                if (ReportedOperationFailures.TryAdd(operation.Definition.InstanceId, 0))
                    Debug.WriteLine($"Focused lyric operation {operation.Definition.InstanceId} failed: {exception}");
            }
        }

        Windows.Foundation.Rect? clip = null;
        if (hasRectangleClip && lineRevealFrame is { } revealFrame)
        {
            var offset = state.Origin - contribution.Cluster.BaseState.Origin;
            var boundary = revealFrame.Sample.Position;
            var rtl = revealFrame.Timeline.IsRightToLeft;
            if (!revealFrame.Timeline.IsSpatiallyMonotone)
            {
                for (var index = 0; index < revealFrame.Timeline.Spans.Count; index++)
                {
                    var span = revealFrame.Timeline.Spans[index];
                    if (span.StartTimeMs != contribution.WordStart || span.EndTimeMs != contribution.WordEnd)
                        continue;
                    boundary = revealFrame.Timeline.SampleSpan(span, revealFrame.SampleTimeMs).Position;
                    rtl = span.Trailing < span.Leading;
                    break;
                }
            }
            var calculated = GetLineVectorContributionClip(
                contribution.Cluster.VisualLeft + offset.X,
                contribution.Cluster.VisualRight + offset.X,
                boundary + offset.X,
                highlighted,
                rtl,
                layout.RenderingHeight);
            if (calculated is { Width: <= 0 }) return;
            if (calculated is { } value)
                clip = new Windows.Foundation.Rect(value.Left, value.Top, value.Width, value.Height);
        }

        GlyphRunDrawHelper.DrawCluster(session, brush, state, clip);
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

    private LineRevealFrame GetLineRevealFrame(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        LyricExpressionLine line,
        LyricExpressionFrame frame)
    {
        var key = new LineRevealFrameKey(operation.Definition.InstanceId,
            contribution.Cluster.Layer, contribution.Cluster.VisualLineIndex);
        if (_lineRevealFrames.TryGetValue(key, out var cached)) return cached;

        var timeline = GetLineRevealTimeline(layout, contribution.Cluster, line);
        var scopeContribution = CreateLineScopeContribution(layout, contribution.Cluster, line, frame.CurrentTimeMs);
        var scopes = CreateScopes(scopeContribution, line, scopeContribution.WordProgress, 0);
        var offset = LineScalar(operation, scopeContribution, "revealTimeOffsetMs", line, frame, scopes, 0);
        var sampleTime = AdjustLineRevealTime(frame.CurrentTimeMs, timeline.StartTimeMs, timeline.EndTimeMs, offset);
        var sample = timeline.Sample(sampleTime);
        scopes = scopes with { Glyph = scopes.Glyph with { RevealProgress = sample.Progress } };
        var feather = Math.Max(0, LineScalar(operation, scopeContribution, "featherDip", line, frame, scopes, 0));
        var result = new LineRevealFrame(timeline, sample, feather, sampleTime);
        _lineRevealFrames.Add(key, result);
        return result;
    }

    private FocusedLineRevealTimeline GetLineRevealTimeline(
        LyricTextLayoutSnapshot layout,
        LyricGlyphCluster cluster,
        LyricExpressionLine line)
    {
        var key = new LineRevealKey(cluster.Layer, cluster.VisualLineIndex);
        if (_lineRevealTimelines.TryGetValue(key, out var cached)) return cached;

        var clusters = ClustersForLayer(layout, cluster.Layer);
        var tokens = layout.Tokens;
        var left = float.MaxValue;
        var right = float.MinValue;
        var tokenBounds = new Dictionary<int, TokenRevealBounds>();
        for (var index = cluster.VisualLineStartClusterIndex;
             index < cluster.VisualLineEndClusterIndexExclusive && index < clusters.Count;
             index++)
        {
            var candidate = clusters[index];
            left = Math.Min(left, candidate.VisualLeft);
            right = Math.Max(right, candidate.VisualRight);
            var tokenIndex = candidate.TokenStartIndex;
            if ((uint)tokenIndex >= (uint)tokens.Count) continue;
            if (tokenBounds.TryGetValue(tokenIndex, out var bounds))
                tokenBounds[tokenIndex] = new TokenRevealBounds(
                    Math.Min(bounds.Left, candidate.VisualLeft),
                    Math.Max(bounds.Right, candidate.VisualRight));
            else
                tokenBounds.Add(tokenIndex, new TokenRevealBounds(candidate.VisualLeft, candidate.VisualRight));
        }

        if (!float.IsFinite(left) || !float.IsFinite(right) || right <= left)
        {
            left = cluster.VisualLeft;
            right = Math.Max(cluster.VisualRight, left + 0.001f);
        }

        var tokenIndexes = new List<int>(tokenBounds.Keys);
        tokenIndexes.Sort();
        var isRightToLeft = tokenIndexes.Count > 1
            ? tokenBounds[tokenIndexes[^1]].Center < tokenBounds[tokenIndexes[0]].Center
            : (cluster.BaseState.BidiLevel & 1) != 0;
        var spans = new List<FocusedLineRevealSpan>(Math.Max(1, tokenIndexes.Count));
        for (var index = 0; index < tokenIndexes.Count; index++)
        {
            var tokenIndex = tokenIndexes[index];
            var token = tokens[tokenIndex];
            var bounds = tokenBounds[tokenIndex];
            spans.Add(new FocusedLineRevealSpan(
                token.StartTime,
                token.EndTime,
                isRightToLeft ? bounds.Right : bounds.Left,
                isRightToLeft ? bounds.Left : bounds.Right));
        }

        if (spans.Count == 0)
            spans.Add(new FocusedLineRevealSpan(line.StartMs, line.EndMs,
                isRightToLeft ? right : left, isRightToLeft ? left : right));

        var timeline = FocusedLineRevealTimeline.Create(spans, left, right, isRightToLeft);
        _lineRevealTimelines.Add(key, timeline);
        return timeline;
    }

    private static Contribution CreateLineScopeContribution(
        LyricTextLayoutSnapshot layout,
        LyricGlyphCluster sourceCluster,
        LyricExpressionLine line,
        long timeMs)
    {
        var clusters = ClustersForLayer(layout, sourceCluster.Layer);
        LyricGlyphCluster? activeCluster = null;
        LyricTextToken? activeToken = null;
        LyricGlyphCluster? previousCluster = null;
        LyricTextToken? previousToken = null;
        LyricGlyphCluster? nextCluster = null;
        LyricTextToken? nextToken = null;
        for (var index = sourceCluster.VisualLineStartClusterIndex;
             index < sourceCluster.VisualLineEndClusterIndexExclusive && index < clusters.Count;
             index++)
        {
            var candidate = clusters[index];
            if ((uint)candidate.TokenStartIndex >= (uint)layout.Tokens.Count) continue;
            var token = layout.Tokens[candidate.TokenStartIndex];
            if (timeMs >= token.StartTime && timeMs < token.EndTime)
            {
                activeCluster = candidate;
                activeToken = token;
                break;
            }
            if (token.StartTime <= timeMs && (previousToken is null || token.StartTime > previousToken.StartTime))
                (previousCluster, previousToken) = (candidate, token);
            if (token.StartTime > timeMs && (nextToken is null || token.StartTime < nextToken.StartTime))
                (nextCluster, nextToken) = (candidate, token);
        }

        var selected = activeCluster ?? previousCluster ?? nextCluster ?? sourceCluster;
        var selectedToken = activeToken ?? previousToken ?? nextToken;
        if (selectedToken is null && (uint)selected.TokenStartIndex < (uint)layout.Tokens.Count)
            selectedToken = layout.Tokens[selected.TokenStartIndex];
        var start = selectedToken?.StartTime ?? line.StartMs;
        var end = selectedToken?.EndTime ?? line.EndMs;
        var progress = WordProgress(timeMs, start, end);
        return new Contribution(selected, Target(selected.Layer, FocusedTargetState.CurrentHighlighted),
            FocusedTargetState.CurrentHighlighted, selectedToken, start, end, progress);
    }

    private static IReadOnlyList<LyricGlyphCluster> ClustersForLayer(
        LyricTextLayoutSnapshot layout,
        LyricTextLayer layer) => layer switch
    {
        LyricTextLayer.Lyric => layout.LyricGlyphClusters,
        LyricTextLayer.Transliteration => layout.TransliterationGlyphClusters,
        _ => layout.TranslationGlyphClusters
    };

    private static long AdjustLineRevealTime(long timeMs, long startMs, long endMs, float offset)
    {
        if (endMs <= startMs) return timeMs;
        var adjustedStart = offset < 0 ? startMs - offset : startMs;
        var adjustedEnd = offset > 0 ? endMs - offset : endMs;
        if (adjustedEnd <= adjustedStart) return timeMs >= adjustedEnd ? endMs : startMs;
        var progress = Math.Clamp((timeMs - adjustedStart) / (float)(adjustedEnd - adjustedStart), 0, 1);
        return startMs + (long)Math.Round((endMs - startMs) * progress);
    }

    public void ReleaseRasterCache()
    {
        _lineRevealResources.Dispose();
        _lineMasks.Clear();
        _lineRevealFrames.Clear();
        foreach (var source in _glyphSources.Values) source.Dispose();
        _glyphSources.Clear();
    }

    private CanvasCommandList GetGlyphSource(
        CanvasDrawingSession session,
        LyricGlyphCluster cluster,
        Windows.UI.Color color)
    {
        if (_glyphSources.TryGetValue(cluster, out var source)) return source;
        source = new CanvasCommandList(session);
        using (var drawing = source.CreateDrawingSession())
        using (var brush = new CanvasSolidColorBrush(drawing, color))
            GlyphRunDrawHelper.DrawCluster(drawing, brush, LyricGlyphDrawState.FromCluster(cluster, color));
        _glyphSources.Add(cluster, source);
        return source;
    }

    private void DrawContribution(
        CanvasDrawingSession session,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame frame)
    {
        var resources = _contributionResources;
        try
        {
            var image = (ICanvasImage)GetGlyphSource(session, contribution.Cluster, layout.FocusingColor);
            var geometryTransform = Matrix3x2.Identity;
            var currentOrigin = contribution.Cluster.BaseState.Origin;
            var revealProgress = contribution.WordProgress;

            for (var operationIndex = 0; operationIndex < profile.Operations.Count; operationIndex++)
            {
                var operation = profile.Operations[operationIndex];
                if (!ShouldApplyOperationToTarget(operation.Definition.TypeId, operation.Targets,
                        contribution.Target)) continue;
                var input = image;
                var inputTransform = geometryTransform;
                var inputOrigin = currentOrigin;
                try
                {
                    var scopes = CreateScopes(contribution, line, revealProgress, 0);
                    switch (operation.Definition.TypeId)
                    {
                        case FocusedTextBuiltInOperationTypes.HighlightReveal:
                            image = ApplyReveal(operation, contribution, layout, input, resources, session,
                                line, frame, scopes, geometryTransform, out revealProgress);
                            break;
                        case FocusedTextBuiltInOperationTypes.Color:
                            image = ApplyColor(operation, contribution, input, resources, line, frame, scopes,
                                new LyricColorValue(layout.FocusingColor.A, layout.FocusingColor.R,
                                    layout.FocusingColor.G, layout.FocusingColor.B));
                            break;
                        case FocusedTextBuiltInOperationTypes.Opacity:
                            image = ApplyOpacity(operation, contribution, input, resources, line, frame, scopes);
                            break;
                        case FocusedTextBuiltInOperationTypes.Transform2D:
                            image = ApplyTransform2D(operation, contribution, input, resources, line, frame, scopes,
                                ref geometryTransform, ref currentOrigin);
                            break;
                        case FocusedTextBuiltInOperationTypes.Transform3D:
                            image = ApplyTransform3D(operation, contribution, input, resources, line, frame, scopes,
                                currentOrigin);
                            break;
                        case FocusedTextBuiltInOperationTypes.GaussianBlur:
                            image = ApplyBlur(operation, contribution, input, resources, line, frame, scopes);
                            break;
                        case FocusedTextBuiltInOperationTypes.Glow:
                            image = ApplyGlow(operation, contribution, input, resources, session, line, frame, scopes);
                            break;
                        case FocusedTextBuiltInOperationTypes.Stroke:
                            image = ApplyStroke(operation, contribution, input, resources, session, line, frame, scopes);
                            break;
                        case FocusedTextBuiltInOperationTypes.GlyphLift:
                            image = ApplyLift(operation, contribution, input, resources, layout,
                                renderContext.CurrentLyricTime, line, frame, revealProgress,
                                ref geometryTransform, ref currentOrigin);
                            break;
                        case FocusedTextBuiltInOperationTypes.DrawScript when operation.DrawScript is not null:
                            image = ApplyScript(operation, contribution, input, resources, session, line, frame,
                                scopes, currentOrigin);
                            break;
                    }
                }
                catch (Exception exception)
                {
                    image = input;
                    geometryTransform = inputTransform;
                    currentOrigin = inputOrigin;
                    if (ReportedOperationFailures.TryAdd(operation.Definition.InstanceId, 0))
                        Debug.WriteLine($"Focused lyric operation {operation.Definition.InstanceId} failed: {exception}");
                }
            }

            session.DrawImage(image);
        }
        finally
        {
            resources.Dispose();
        }
    }

    private ICanvasImage ApplyReveal(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        CanvasDrawingSession session,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        Matrix3x2 geometryTransform,
        out float revealProgress)
    {
        revealProgress = contribution.WordProgress;
        var options = RevealOptions.From(operation.Definition);
        if (options.Mode != HighlightRevealMode.RectangleClip &&
            contribution.State is not (FocusedTargetState.CurrentHighlighted or FocusedTargetState.CurrentPending))
            return input;

        var offset = Scalar(operation, contribution, "revealTimeOffsetMs", line, frame, scopes, 0);
        var adjustedWordProgress = GetRevealWordProgress(
            frame.CurrentTimeMs,
            contribution.WordStart,
            contribution.WordEnd,
            offset);
        revealProgress = FocusedTextProgress.GetRevealProgress(
            options.Mode,
            adjustedWordProgress,
            contribution.Cluster.TokenClusterIndex,
            contribution.Cluster.TokenClusterCount);
        var highlighted = IsHighlightedContribution(contribution.State);

        if (options.Mode != HighlightRevealMode.RectangleClip)
        {
            var opacity = highlighted ? revealProgress : 1 - revealProgress;
            var effect = resources.Track(new OpacityEffect { Source = input, Opacity = opacity });
            return effect;
        }

        var lineFrame = GetLineRevealFrame(operation, contribution, layout, line, frame);
        return ApplyLineRectangleMask(input, resources, session, layout, contribution.Cluster,
            geometryTransform, lineFrame, highlighted);
    }

    private ICanvasImage ApplyLineRectangleMask(
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        LyricGlyphCluster cluster,
        Matrix3x2 geometryTransform,
        LineRevealFrame lineFrame,
        bool highlighted)
    {
        var timeline = lineFrame.Timeline;
        var left = timeline.Left;
        var width = timeline.Width;
        var maskPlan = CreateRectangleMaskPlan(
            lineFrame.Sample.Progress,
            highlighted,
            timeline.IsRightToLeft,
            lineFrame.Feather,
            width);

        if (maskPlan.ConstantOpacity is { } constantOpacity)
        {
            return constantOpacity >= 1
                ? input
                : resources.Track(new OpacityEffect { Source = input, Opacity = 0 });
        }

        var maskKey = new LineMaskKey(cluster.Layer, cluster.VisualLineIndex, highlighted);
        if (!_lineMasks.TryGetValue(maskKey, out var mask))
        {
            mask = _lineRevealResources.Track(new CanvasCommandList(session));
            using var maskSession = mask.CreateDrawingSession();
            if (!timeline.IsSpatiallyMonotone)
            {
                DrawMixedDirectionLineMask(maskSession, timeline, lineFrame, highlighted, layout.RenderingHeight);
            }
            else if (lineFrame.Feather <= 0)
            {
                var boundary = lineFrame.Sample.Position;
                var clipLeft = highlighted == timeline.IsRightToLeft ? boundary : 0;
                var clipRight = highlighted == timeline.IsRightToLeft ? layout.RenderingWidth : boundary;
                if (clipRight > clipLeft)
                    maskSession.FillRectangle(clipLeft, 0, clipRight - clipLeft, layout.RenderingHeight, Colors.White);
            }
            else
            {
                using var brush = new CanvasLinearGradientBrush(maskSession,
                [
                    new CanvasGradientStop { Position = 0, Color = MaskColor(maskPlan.StartOpacity) },
                    new CanvasGradientStop { Position = maskPlan.FirstStop, Color = MaskColor(maskPlan.FirstOpacity) },
                    new CanvasGradientStop { Position = maskPlan.MiddleStop, Color = MaskColor(maskPlan.MiddleOpacity) },
                    new CanvasGradientStop { Position = maskPlan.SecondStop, Color = MaskColor(maskPlan.SecondOpacity) },
                    new CanvasGradientStop { Position = 1, Color = MaskColor(maskPlan.EndOpacity) }
                ])
                {
                    StartPoint = new Vector2(left, 0),
                    EndPoint = new Vector2(left + width, 0)
                };
                maskSession.FillRectangle(0, 0, layout.RenderingWidth, layout.RenderingHeight, brush);
            }

            _lineMasks.Add(maskKey, mask);
        }

        ICanvasImage alphaMask = mask;
        if (geometryTransform != Matrix3x2.Identity)
            alphaMask = resources.Track(new Transform2DEffect { Source = mask, TransformMatrix = geometryTransform });
        return resources.Track(new AlphaMaskEffect { Source = input, AlphaMask = alphaMask });
    }

    private static void DrawMixedDirectionLineMask(
        CanvasDrawingSession session,
        FocusedLineRevealTimeline timeline,
        LineRevealFrame lineFrame,
        bool highlighted,
        float height)
    {
        for (var index = 0; index < timeline.Spans.Count; index++)
        {
            var span = timeline.Spans[index];
            var sample = timeline.SampleSpan(span, lineFrame.SampleTimeMs);
            var left = Math.Min(span.Leading, span.Trailing);
            var right = Math.Max(span.Leading, span.Trailing);
            var width = Math.Max(right - left, 0.001f);
            var rtl = span.Trailing < span.Leading;
            var plan = CreateRectangleMaskPlan(sample.Progress, highlighted, rtl, lineFrame.Feather, width);
            if (plan.ConstantOpacity is <= 0) continue;
            if (plan.ConstantOpacity is >= 1)
            {
                session.FillRectangle(left, 0, width, height, Colors.White);
                continue;
            }

            if (lineFrame.Feather <= 0)
            {
                var clipLeft = highlighted == rtl ? sample.Position : left;
                var clipRight = highlighted == rtl ? right : sample.Position;
                if (clipRight > clipLeft) session.FillRectangle(clipLeft, 0, clipRight - clipLeft, height, Colors.White);
                continue;
            }

            using var brush = new CanvasLinearGradientBrush(session,
            [
                new CanvasGradientStop { Position = 0, Color = MaskColor(plan.StartOpacity) },
                new CanvasGradientStop { Position = plan.FirstStop, Color = MaskColor(plan.FirstOpacity) },
                new CanvasGradientStop { Position = plan.MiddleStop, Color = MaskColor(plan.MiddleOpacity) },
                new CanvasGradientStop { Position = plan.SecondStop, Color = MaskColor(plan.SecondOpacity) },
                new CanvasGradientStop { Position = 1, Color = MaskColor(plan.EndOpacity) }
            ])
            {
                StartPoint = new Vector2(left, 0),
                EndPoint = new Vector2(right, 0)
            };
            session.FillRectangle(left, 0, width, height, brush);
        }
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
        var rampEnd = boundaryProgress + normalizedFeather;
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
            // Settle the final visual line frames toward a fully solid mask without
            // strengthening feather spill at ordinary token boundaries.
            var terminalGain = MathF.Pow(boundaryProgress, 8);
            var highlightOpacity = terminalGain + (1 - terminalGain) * featherOpacity;
            return highlighted ? highlightOpacity : 1 - highlightOpacity;
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

    private static Color MaskColor(float opacity) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(opacity * byte.MaxValue), byte.MinValue, byte.MaxValue),
            byte.MaxValue, byte.MaxValue, byte.MaxValue);

    private ICanvasImage ApplyColor(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        LyricColorValue fallback)
    {
        var value = ColorValue(operation, contribution, "color", line, frame, scopes, fallback);
        var source = resources.Track(new ColorSourceEffect
        {
            Color = Color.FromArgb(value.A, value.R, value.G, value.B)
        });
        return resources.Track(new AlphaMaskEffect { Source = source, AlphaMask = input });
    }

    private ICanvasImage ApplyOpacity(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes) => resources.Track(new OpacityEffect
        {
            Source = input,
            Opacity = Scalar(operation, contribution, "opacity", line, frame, scopes, 1)
        });

    private ICanvasImage ApplyTransform2D(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        ref Matrix3x2 geometryTransform,
        ref Vector2 currentOrigin)
    {
        var x = Scalar(operation, contribution, "x", line, frame, scopes, 0);
        var y = Scalar(operation, contribution, "y", line, frame, scopes, 0);
        var scaleX = Scalar(operation, contribution, "scaleX", line, frame, scopes, 1);
        var scaleY = Scalar(operation, contribution, "scaleY", line, frame, scopes, 1);
        var rotation = Scalar(operation, contribution, "rotation", line, frame, scopes, 0);
        var anchor = contribution.Cluster.BaseState.Origin + new Vector2(
            Scalar(operation, contribution, "anchorX", line, frame, scopes, 0),
            Scalar(operation, contribution, "anchorY", line, frame, scopes, 0));
        var matrix = Matrix3x2.CreateTranslation(-anchor) *
                     Matrix3x2.CreateScale(scaleX, scaleY) *
                     Matrix3x2.CreateRotation(MathF.PI * rotation / 180f) *
                     Matrix3x2.CreateTranslation(anchor + new Vector2(x, y));
        geometryTransform *= matrix;
        currentOrigin = Vector2.Transform(currentOrigin, matrix);
        return resources.Track(new Transform2DEffect { Source = input, TransformMatrix = matrix });
    }

    private ICanvasImage ApplyTransform3D(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        Vector2 currentOrigin)
    {
        var angleX = Scalar(operation, contribution, "angleX", line, frame, scopes, 0);
        var angleY = Scalar(operation, contribution, "angleY", line, frame, scopes, 0);
        var angleZ = Scalar(operation, contribution, "angleZ", line, frame, scopes, 0);
        var depth = Scalar(operation, contribution, "depth", line, frame, scopes, 3000);
        var center = new Vector3(currentOrigin + new Vector2(
            Scalar(operation, contribution, "anchorX", line, frame, scopes, 0),
            Scalar(operation, contribution, "anchorY", line, frame, scopes, 0)), 0);
        var perspective = Matrix4x4.Identity;
        perspective.M34 = 1f / Math.Max(depth, 1);
        return resources.Track(new Transform3DEffect
        {
            Source = input,
            TransformMatrix = Matrix4x4.CreateTranslation(-center) *
                              Matrix4x4.CreateRotationX(MathF.PI * angleX / 180f) *
                              Matrix4x4.CreateRotationY(MathF.PI * angleY / 180f) *
                              Matrix4x4.CreateRotationZ(MathF.PI * angleZ / 180f) *
                              perspective * Matrix4x4.CreateTranslation(center)
        });
    }

    private ICanvasImage ApplyBlur(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes) => resources.Track(new GaussianBlurEffect
        {
            Source = input,
            BlurAmount = Scalar(operation, contribution, "amount", line, frame, scopes, 0),
            BorderMode = EffectBorderMode.Soft
        });

    private ICanvasImage ApplyGlow(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        CanvasDrawingSession session,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes)
    {
        var color = ColorValue(operation, contribution, "color", line, frame, scopes, line.FocusingColor);
        var shadow = resources.Track(new ShadowEffect
        {
            Source = input,
            BlurAmount = Scalar(operation, contribution, "blur", line, frame, scopes, 4),
            ShadowColor = Color.FromArgb(color.A, color.R, color.G, color.B)
        });
        var opacity = resources.Track(new OpacityEffect
        {
            Source = shadow,
            Opacity = Scalar(operation, contribution, "opacity", line, frame, scopes, 0.4f)
        });
        var result = resources.Track(new CanvasCommandList(session));
        using var drawing = result.CreateDrawingSession();
        drawing.DrawImage(opacity,
            Scalar(operation, contribution, "x", line, frame, scopes, 0),
            Scalar(operation, contribution, "y", line, frame, scopes, 0));
        drawing.DrawImage(input);
        return result;
    }

    private ICanvasImage ApplyStroke(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        CanvasDrawingSession session,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes)
    {
        var width = Scalar(operation, contribution, "width", line, frame, scopes, 1);
        var color = ColorValue(operation, contribution, "color", line, frame, scopes, line.FocusingColor);
        var source = resources.Track(new ColorSourceEffect { Color = Color.FromArgb(color.A, color.R, color.G, color.B) });
        var strokeMask = resources.Track(new AlphaMaskEffect { Source = source, AlphaMask = input });
        var result = resources.Track(new CanvasCommandList(session));
        using var drawing = result.CreateDrawingSession();
        ReadOnlySpan<Vector2> directions =
        [
            new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
            new(-0.707f, -0.707f), new(0.707f, -0.707f),
            new(-0.707f, 0.707f), new(0.707f, 0.707f)
        ];
        foreach (var direction in directions) drawing.DrawImage(strokeMask, direction * width);
        drawing.DrawImage(input);
        return result;
    }

    private ICanvasImage ApplyLift(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        LyricTextLayoutSnapshot layout,
        long timeMs,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        float revealProgress,
        ref Matrix3x2 geometryTransform,
        ref Vector2 currentOrigin)
    {
        if (ResolveLiftWord(operation.Definition, contribution, layout, line, timeMs) is not { } word) return input;
        var baseScopes = CreateScopes(contribution, line, revealProgress, 0, word);
        var offset = Scalar(operation, contribution, "liftTimeOffsetMs", line, frame, baseScopes, 0);
        var finish = Scalar(operation, contribution, "liftFinishDurationMs", line, frame, baseScopes, 0);
        var motion = Option(operation.Definition, "motion", GlyphLiftMotion.Hold);
        var raw = FocusedTextProgress.GetTimedProgress(timeMs, word.Start, word.End, offset, finish, motion);
        var unit = word.Exists
            ? Option(operation.Definition, "liftUnit", GlyphLiftUnit.Auto)
            : GlyphLiftUnit.Word;
        var overlap = Scalar(operation, contribution, "overlap", line, frame, baseScopes, 0);
        var threshold = Scalar(operation, contribution, "wholeWordThresholdMs", line, frame, baseScopes, 1000);
        var local = FocusedTextProgress.GetLiftProgress(
            raw,
            word.End - word.Start,
            word.GlyphIndex,
            word.GlyphCount,
            overlap,
            threshold,
            unit,
            motion);
        var liftProgress = GetEasedLiftProgress(operation, contribution, line, frame, baseScopes, local);
        var scopes = baseScopes with { Glyph = baseScopes.Glyph with { LiftProgress = liftProgress } };
        var height = Scalar(operation, contribution, "height", line, frame, scopes, 3);
        var matrix = Matrix3x2.CreateTranslation(0, -height * liftProgress);
        geometryTransform *= matrix;
        currentOrigin = Vector2.Transform(currentOrigin, matrix);
        return resources.Track(new Transform2DEffect { Source = input, TransformMatrix = matrix });
    }

    private void ApplyLiftVector(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        long timeMs,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        float revealProgress,
        ref LyricGlyphDrawState state,
        ref Vector2 currentOrigin)
    {
        if (ResolveLiftWord(operation.Definition, contribution, layout, line, timeMs) is not { } word) return;
        var baseScopes = CreateScopes(contribution, line, revealProgress, 0, word);
        var offset = Scalar(operation, contribution, "liftTimeOffsetMs", line, frame, baseScopes, 0);
        var finish = Scalar(operation, contribution, "liftFinishDurationMs", line, frame, baseScopes, 0);
        var motion = Option(operation.Definition, "motion", GlyphLiftMotion.Hold);
        var raw = FocusedTextProgress.GetTimedProgress(timeMs, word.Start, word.End, offset, finish, motion);
        var unit = word.Exists
            ? Option(operation.Definition, "liftUnit", GlyphLiftUnit.Auto)
            : GlyphLiftUnit.Word;
        var overlap = Scalar(operation, contribution, "overlap", line, frame, baseScopes, 0);
        var threshold = Scalar(operation, contribution, "wholeWordThresholdMs", line, frame, baseScopes, 1000);
        var local = FocusedTextProgress.GetLiftProgress(raw, word.End - word.Start, word.GlyphIndex,
            word.GlyphCount, overlap, threshold, unit, motion);
        var liftProgress = GetEasedLiftProgress(operation, contribution, line, frame, baseScopes, local);
        var scopes = baseScopes with { Glyph = baseScopes.Glyph with { LiftProgress = liftProgress } };
        var height = Scalar(operation, contribution, "height", line, frame, scopes, 3);
        var translation = new Vector2(0, -height * liftProgress);
        state.Origin += translation;
        currentOrigin += translation;
    }

    private float GetEasedLiftProgress(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        float progress)
    {
        var easingId = operation.Definition.Options.GetValueOrDefault("easingId", "linear");
        if (string.Equals(easingId, "linear", StringComparison.OrdinalIgnoreCase)) return progress;
        if (operation.ConstantLiftEasing is { } constantEasing)
            return (float)constantEasing.Ease(progress);

        return (float)LyricEasingFactory.Evaluate(
            easingId,
            operation.Definition.Options.GetValueOrDefault("easingMode", "in"),
            progress,
            Scalar(operation, contribution, "exponent", line, frame, scopes, 2),
            Scalar(operation, contribution, "springiness", line, frame, scopes, 3),
            Scalar(operation, contribution, "oscillations", line, frame, scopes, 3),
            Scalar(operation, contribution, "bounces", line, frame, scopes, 2),
            Scalar(operation, contribution, "bounciness", line, frame, scopes, 2));
    }

    private ICanvasImage ApplyScript(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        CanvasDrawingSession session,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        Vector2 currentOrigin)
    {
        var result = resources.Track(new CanvasCommandList(session));
        using var drawing = result.CreateDrawingSession();
        void Execute()
        {
            var original = drawing.Transform;
            try
            {
                drawing.Transform = Matrix3x2.CreateTranslation(currentOrigin) * original;
                var context = new LyricDrawExecutionContext(drawing);
                foreach (var command in operation.DrawScript!.Commands)
                    command.Execute(context, line, frame, scopes.Text, scopes.Word, scopes.Glyph,
                        _expressionFrameCache);
                context.EnsureBalanced();
            }
            finally
            {
                drawing.Transform = original;
            }
        }

        if (operation.DrawScript!.Placement == FocusedDrawScriptPlacement.BehindGlyph) Execute();
        drawing.DrawImage(input);
        if (operation.DrawScript.Placement == FocusedDrawScriptPlacement.AboveGlyph) Execute();
        return result;
    }

    private float Scalar(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        string key,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        float fallback)
    {
        var transitionKey = new FocusedTransitionKey(operation.Definition.InstanceId, key, contribution.Target,
            contribution.Cluster.LayerClusterIndex);
        return EvaluateScalar(operation, key, line, frame, scopes, fallback, transitionKey);
    }

    private float LineScalar(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        string key,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        float fallback)
    {
        var transitionKey = new FocusedTransitionKey(operation.Definition.InstanceId, key,
            $"line:{contribution.Cluster.Layer}", contribution.Cluster.VisualLineIndex);
        return EvaluateScalar(operation, key, line, frame, scopes, fallback, transitionKey);
    }

    private float EvaluateScalar(
        CompiledFocusedTextOperation operation,
        string key,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        float fallback,
        FocusedTransitionKey transitionKey)
    {
        if (!operation.Scalars.TryGetValue(key, out var parameter)) return fallback;
        var compiled = parameter.Value;
        var value = compiled.ConstantValue ?? _expressionFrameCache.EvaluateScalar(
            compiled.CacheId, compiled.Dependencies, compiled.Expression,
            line, frame, scopes.Text, scopes.Word, scopes.Glyph);
        if (!float.IsFinite(value)) throw new InvalidOperationException("表达式返回了 NaN 或 Infinity。");
        if (parameter.Minimum is { } minimum) value = Math.Max(value, minimum);
        if (parameter.Maximum is { } maximum) value = Math.Min(value, maximum);
        // A context-free expression cannot change while this renderer is alive, so a
        // transition would only allocate one inert state per target/GlyphUnit.
        if (parameter.Transition is null || compiled.ConstantValue.HasValue) return value;
        if (!_scalarTransitions.TryGetValue(transitionKey, out var state))
            _scalarTransitions[transitionKey] = state = new ScalarTransitionState();
        return state.Animate(value, parameter.Transition, line, frame, scopes, _expressionFrameCache);
    }

    private LyricColorValue ColorValue(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
        string key,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        LyricColorValue fallback)
    {
        if (!operation.Colors.TryGetValue(key, out var parameter)) return fallback;
        var compiled = parameter.Value;
        var value = compiled.ConstantValue ?? _expressionFrameCache.EvaluateColor(
            compiled.CacheId, compiled.Dependencies, compiled.Expression,
            line, frame, scopes.Text, scopes.Word, scopes.Glyph);
        if (parameter.Transition is null || compiled.ConstantValue.HasValue) return value;
        var keyValue = new FocusedTransitionKey(operation.Definition.InstanceId, key, contribution.Target,
            contribution.Cluster.LayerClusterIndex);
        if (!_colorTransitions.TryGetValue(keyValue, out var state))
            _colorTransitions[keyValue] = state = new ColorTransitionState();
        return state.Animate(value, parameter.Transition, line, frame, scopes, _expressionFrameCache);
    }

    private static ContributionSet CreateContributions(
        LyricGlyphCluster cluster,
        LyricTextLayer layer,
        LyricTextLayoutSnapshot layout,
        long timeMs,
        RevealOptions options,
        LyricExpressionLine line)
    {
        if (layer == LyricTextLayer.Translation)
            return new ContributionSet(new Contribution(cluster, FocusedTextTargets.Translation,
                FocusedTargetState.Highlighted, null, line.StartMs, line.EndMs, line.Progress));

        var tokens = layout.Tokens;
        var useWords = layout.HasRealWords || options.UntimedMode == UntimedHighlightMode.InferWords;
        if (layer == LyricTextLayer.Transliteration && options.TransliterationMode == TransliterationProgressMode.WholeLine)
            useWords = false;
        if (!useWords || tokens.Count == 0)
        {
            var highlighted = options.UntimedMode == UntimedHighlightMode.WholeLine ||
                              layer == LyricTextLayer.Transliteration &&
                              options.TransliterationMode == TransliterationProgressMode.WholeLine;
            var state = highlighted ? FocusedTargetState.Highlighted : FocusedTargetState.Unhighlighted;
            return new ContributionSet(new Contribution(cluster, Target(layer, state), state, null,
                line.StartMs, line.EndMs, highlighted ? 1 : 0));
        }

        var tokenIndex = Math.Clamp(cluster.TokenStartIndex, 0, tokens.Count - 1);
        var token = tokens[tokenIndex];
        var duration = token.EndTime - token.StartTime;
        var progress = duration <= 0
            ? timeMs >= token.StartTime ? 1 : 0
            : Math.Clamp((timeMs - token.StartTime) / (float)duration, 0, 1);
        if (options.Mode == HighlightRevealMode.RectangleClip)
        {
            var highlightedState = timeMs >= token.EndTime
                ? FocusedTargetState.Highlighted
                : FocusedTargetState.CurrentHighlighted;
            var pendingState = timeMs < token.StartTime
                ? FocusedTargetState.Unhighlighted
                : FocusedTargetState.CurrentPending;
            return new ContributionSet(
                new Contribution(cluster, Target(layer, pendingState), pendingState,
                    token, token.StartTime, token.EndTime, progress),
                new Contribution(cluster, Target(layer, highlightedState), highlightedState,
                    token, token.StartTime, token.EndTime, progress));
        }

        if (timeMs < token.StartTime)
            return new ContributionSet(new Contribution(cluster, Target(layer, FocusedTargetState.Unhighlighted),
                FocusedTargetState.Unhighlighted, token, token.StartTime, token.EndTime, 0));
        if (timeMs >= token.EndTime)
            return new ContributionSet(new Contribution(cluster, Target(layer, FocusedTargetState.Highlighted),
                FocusedTargetState.Highlighted, token, token.StartTime, token.EndTime, 1));

        return new ContributionSet(
            new Contribution(cluster, Target(layer, FocusedTargetState.CurrentPending),
                FocusedTargetState.CurrentPending, token, token.StartTime, token.EndTime, progress),
            new Contribution(cluster, Target(layer, FocusedTargetState.CurrentHighlighted),
                FocusedTargetState.CurrentHighlighted, token, token.StartTime, token.EndTime, progress));
    }

    private static LiftWord? ResolveLiftWord(
        FocusedTextOperationDefinition definition,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        LyricExpressionLine line,
        long timeMs)
    {
        var mode = Option(definition, "untimedMode", UntimedLiftMode.DoNotLift);
        if (contribution.Cluster.Layer != LyricTextLayer.Translation && layout.HasRealWords)
        {
            var tokenIndex = Math.Clamp(contribution.Cluster.TokenStartIndex, 0, layout.Tokens.Count - 1);
            return LiftWord.FromToken(layout.Tokens[tokenIndex], contribution.Cluster, tokenIndex, layout.Tokens.Count, timeMs);
        }

        if (mode == UntimedLiftMode.DoNotLift) return null;
        if (mode == UntimedLiftMode.WholeLine)
            return new LiftWord(line.StartMs, line.EndMs,
                contribution.Cluster.LayerClusterIndex, contribution.Cluster.LayerClusterCount, false, -1, 0, false,
                line.Progress);

        if (contribution.Cluster.Layer == LyricTextLayer.Lyric && layout.Tokens.Count > 0)
        {
            var tokenIndex = Math.Clamp(contribution.Cluster.TokenStartIndex, 0, layout.Tokens.Count - 1);
            return LiftWord.FromToken(layout.Tokens[tokenIndex], contribution.Cluster, tokenIndex, layout.Tokens.Count, timeMs);
        }
        var inferred = contribution.Cluster.Layer switch
        {
            LyricTextLayer.Transliteration => layout.InferredTransliterationTokens,
            LyricTextLayer.Translation => layout.InferredTranslationTokens,
            _ => layout.Tokens
        };
        var inferredTokenIndex = contribution.Cluster.InferredTokenIndex;
        if ((uint)inferredTokenIndex >= (uint)inferred.Count) return null;
        var token = inferred[inferredTokenIndex];
        return new LiftWord(token.StartTime, token.EndTime,
            contribution.Cluster.InferredTokenClusterIndex,
            Math.Max(1, contribution.Cluster.InferredTokenClusterCount),
            true, inferredTokenIndex, inferred.Count, true, WordProgress(timeMs, token.StartTime, token.EndTime));
    }

    private static ExpressionScopes CreateScopes(
        Contribution contribution,
        LyricExpressionLine line,
        float revealProgress,
        float liftProgress,
        LiftWord? liftWord = null)
    {
        var cluster = contribution.Cluster;
        var word = liftWord ?? (contribution.Word is null
            ? null
            : LiftWord.FromToken(contribution.Word, cluster, cluster.TokenStartIndex,
                Math.Max(cluster.TokenEndIndexExclusive, cluster.TokenStartIndex + 1),
                contribution.WordStart + (long)((contribution.WordEnd - contribution.WordStart) * contribution.WordProgress)));
        var origin = cluster.BaseState.Origin;
        var wordScope = word is not { } resolved
            ? new FocusedTextExpressionWord(false, -1, 0, line.StartMs, line.EndMs,
                line.Progress, false)
            : new FocusedTextExpressionWord(resolved.Exists, resolved.Index, resolved.Count,
                resolved.Start, resolved.End, resolved.Progress, resolved.IsInferred);
        return new ExpressionScopes(
            new FocusedTextExpressionText(
                cluster.Layer == LyricTextLayer.Lyric,
                cluster.Layer == LyricTextLayer.Transliteration,
                cluster.Layer == LyricTextLayer.Translation),
            wordScope,
            new FocusedTextExpressionGlyph(
                cluster.LayerClusterIndex,
                cluster.LayerClusterCount,
                word?.GlyphIndex ?? cluster.LayerClusterIndex,
                word?.GlyphCount ?? cluster.LayerClusterCount,
                revealProgress,
                liftProgress,
                cluster.VisualLeft - origin.X,
                cluster.VisualTop - origin.Y,
                cluster.VisualRight - origin.X,
                cluster.VisualBottom - origin.Y,
                cluster.VisualWidth,
                cluster.VisualHeight));
    }

    private static float GetRevealWordProgress(long time, long start, long end, float offset)
    {
        var adjustedStart = offset < 0 ? start - offset : start;
        var adjustedEnd = offset > 0 ? end - offset : end;
        if (adjustedEnd <= adjustedStart) return time >= (offset > 0 ? start : end) ? 1 : 0;
        return Math.Clamp((time - adjustedStart) / (float)(adjustedEnd - adjustedStart), 0, 1);
    }

    private static float WordProgress(long time, long start, long end) => end <= start
        ? time >= start ? 1 : 0
        : Math.Clamp((time - start) / (float)(end - start), 0, 1);

    private static T Option<T>(FocusedTextOperationDefinition definition, string key, T fallback)
        where T : struct, Enum => definition.Options.TryGetValue(key, out var value) &&
                                  Enum.TryParse<T>(value, true, out var parsed)
        ? parsed
        : fallback;

    private static string Target(LyricTextLayer layer, FocusedTargetState state) => (layer, state) switch
    {
        (LyricTextLayer.Lyric, FocusedTargetState.Highlighted) => FocusedTextTargets.LyricHighlighted,
        (LyricTextLayer.Lyric, FocusedTargetState.CurrentHighlighted) => FocusedTextTargets.LyricCurrentHighlighted,
        (LyricTextLayer.Lyric, FocusedTargetState.CurrentPending) => FocusedTextTargets.LyricCurrentPending,
        (LyricTextLayer.Lyric, _) => FocusedTextTargets.LyricUnhighlighted,
        (LyricTextLayer.Transliteration, FocusedTargetState.Highlighted) => FocusedTextTargets.TransliterationHighlighted,
        (LyricTextLayer.Transliteration, FocusedTargetState.CurrentHighlighted) => FocusedTextTargets.TransliterationCurrentHighlighted,
        (LyricTextLayer.Transliteration, FocusedTargetState.CurrentPending) => FocusedTextTargets.TransliterationCurrentPending,
        _ => FocusedTextTargets.TransliterationUnhighlighted
    };

    private enum FocusedTargetState
    {
        Highlighted,
        CurrentHighlighted,
        CurrentPending,
        Unhighlighted
    }

    private static bool IsHighlightedContribution(FocusedTargetState state) =>
        state is FocusedTargetState.Highlighted or FocusedTargetState.CurrentHighlighted;

    internal static bool ShouldApplyOperationToTarget(
        string typeId,
        IReadOnlySet<string> targets,
        string target) => typeId == FocusedTextBuiltInOperationTypes.HighlightReveal
        ? target != FocusedTextTargets.Translation
        : targets.Contains(target);

    internal static bool ShouldUseLineRevealForTarget(
        HighlightRevealMode mode,
        IReadOnlySet<string> targets,
        string target) => mode == HighlightRevealMode.RectangleClip &&
                          ShouldApplyOperationToTarget(
                              FocusedTextBuiltInOperationTypes.HighlightReveal,
                              targets,
                              target);

    private readonly record struct Contribution(
        LyricGlyphCluster Cluster,
        string Target,
        FocusedTargetState State,
        LyricTextToken? Word,
        long WordStart,
        long WordEnd,
        float WordProgress);

    private readonly record struct LineRevealKey(LyricTextLayer Layer, int VisualLineIndex);

    private readonly record struct LineRevealFrameKey(
        string OperationId,
        LyricTextLayer Layer,
        int VisualLineIndex);

    private readonly record struct LineMaskKey(
        LyricTextLayer Layer,
        int VisualLineIndex,
        bool Highlighted);

    private readonly record struct LineRevealFrame(
        FocusedLineRevealTimeline Timeline,
        FocusedLineRevealSample Sample,
        float Feather,
        long SampleTimeMs);

    private readonly record struct TokenRevealBounds(float Left, float Right)
    {
        public float Center => (Left + Right) / 2;
    }

    private readonly record struct ContributionSet(Contribution First, Contribution? Second = null);

    internal readonly record struct RectangleMaskPlan(
        float? ConstantOpacity,
        float FirstStop,
        float MiddleStop,
        float SecondStop,
        float StartOpacity,
        float FirstOpacity,
        float MiddleOpacity,
        float SecondOpacity,
        float EndOpacity);

    private readonly record struct LiftWord(
        long Start,
        long End,
        int GlyphIndex,
        int GlyphCount,
        bool Exists,
        int Index,
        int Count,
        bool IsInferred,
        float Progress)
    {
        public static LiftWord FromToken(
            LyricTextToken token,
            LyricGlyphCluster cluster,
            int index,
            int count,
            long timeMs) => new(
            token.StartTime, token.EndTime, cluster.TokenClusterIndex, cluster.TokenClusterCount,
            true, index, count, token.IsInferred, WordProgress(timeMs, token.StartTime, token.EndTime));
    }

    private readonly record struct ExpressionScopes(
        FocusedTextExpressionText Text,
        FocusedTextExpressionWord Word,
        FocusedTextExpressionGlyph Glyph);

    private readonly record struct RevealOptions(
        UntimedHighlightMode UntimedMode,
        HighlightRevealMode Mode,
        TransliterationProgressMode TransliterationMode)
    {
        public static RevealOptions From(FocusedTextOperationDefinition? definition) => definition is null
            ? new RevealOptions(UntimedHighlightMode.WholeLine, HighlightRevealMode.RectangleClip,
                TransliterationProgressMode.FollowMain)
            : new RevealOptions(
                Option(definition, "untimedMode", UntimedHighlightMode.WholeLine),
                Option(definition, "revealMode", HighlightRevealMode.RectangleClip),
                Option(definition, "transliterationMode", TransliterationProgressMode.FollowMain));
    }

    private readonly record struct FocusedTransitionKey(
        string InstanceId,
        string Parameter,
        string Target,
        int GlyphIndex);

    private sealed class ScalarTransitionState
    {
        private bool _initialized;
        private float _start;
        private float _target;
        private long _startTime;
        private TransitionSnapshot _snapshot;

        public float Animate(float target, CompiledFocusedTransition transition,
            LyricExpressionLine line, LyricExpressionFrame frame, ExpressionScopes scopes,
            FocusedTextExpressionFrameCache expressionCache)
        {
            if (!_initialized)
            {
                _initialized = true;
                _start = _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes, expressionCache);
                return target;
            }
            if (target != _target)
            {
                _start = Lerp(_start, _target, Progress(frame.CurrentTimeMs));
                _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes, expressionCache);
            }
            return Lerp(_start, _target, Progress(frame.CurrentTimeMs));
        }

        private double Progress(long time) => _snapshot.Duration <= 0
            ? 1
            : _snapshot.Ease(Math.Clamp((time - _startTime) / _snapshot.Duration, 0, 1));
    }

    private sealed class ColorTransitionState
    {
        private bool _initialized;
        private LyricColorValue _start;
        private LyricColorValue _target;
        private long _startTime;
        private TransitionSnapshot _snapshot;

        public LyricColorValue Animate(LyricColorValue target, CompiledFocusedTransition transition,
            LyricExpressionLine line, LyricExpressionFrame frame, ExpressionScopes scopes,
            FocusedTextExpressionFrameCache expressionCache)
        {
            if (!_initialized)
            {
                _initialized = true;
                _start = _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes, expressionCache);
                return target;
            }
            if (target != _target)
            {
                _start = Lerp(_start, _target, Progress(frame.CurrentTimeMs));
                _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes, expressionCache);
            }
            return Lerp(_start, _target, Progress(frame.CurrentTimeMs));
        }

        private double Progress(long time) => _snapshot.Duration <= 0
            ? 1
            : _snapshot.Ease(Math.Clamp((time - _startTime) / _snapshot.Duration, 0, 1));
    }

    private static TransitionSnapshot Snapshot(
        CompiledFocusedTransition transition,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        FocusedTextExpressionFrameCache expressionCache)
    {
        var duration = Evaluate(transition.Duration, line, frame, scopes, expressionCache);
        if (transition.ConstantEasing is { } constantEasing)
            return new TransitionSnapshot(duration, constantEasing, transition.EasingId, transition.Mode,
                2, 6, 1, 3, 2);

        double exponent = 2, springiness = 6, oscillations = 1, bounces = 3, bounciness = 2;
        foreach (var (key, expression) in transition.Arguments)
        {
            var value = Evaluate(expression, line, frame, scopes, expressionCache);
            if (key.Equals("exponent", StringComparison.OrdinalIgnoreCase)) exponent = value;
            else if (key.Equals("springiness", StringComparison.OrdinalIgnoreCase)) springiness = value;
            else if (key.Equals("oscillations", StringComparison.OrdinalIgnoreCase)) oscillations = value;
            else if (key.Equals("bounces", StringComparison.OrdinalIgnoreCase)) bounces = value;
            else if (key.Equals("bounciness", StringComparison.OrdinalIgnoreCase)) bounciness = value;
        }
        return new TransitionSnapshot(duration, null, transition.EasingId, transition.Mode,
            exponent, springiness, oscillations, bounces, bounciness);
    }

    private static float Evaluate(
        CompiledFocusedScalarValue value,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes,
        FocusedTextExpressionFrameCache expressionCache) =>
        value.ConstantValue ?? expressionCache.EvaluateScalar(
            value.CacheId, value.Dependencies, value.Expression,
            line, frame, scopes.Text, scopes.Word, scopes.Glyph);

    private static float Lerp(float start, float end, double progress) =>
        (float)(start + (end - start) * progress);

    private static LyricColorValue Lerp(LyricColorValue start, LyricColorValue end, double progress) => new(
        Channel(start.A, end.A, progress), Channel(start.R, end.R, progress),
        Channel(start.G, end.G, progress), Channel(start.B, end.B, progress));

    private static byte Channel(byte start, byte end, double progress) =>
        (byte)Math.Clamp(Math.Round(start + (end - start) * progress), byte.MinValue, byte.MaxValue);

    private readonly record struct TransitionSnapshot(
        double Duration,
        HyPlayer.LyricRenderer.Animator.EaseFunctionBase? ConstantEasing,
        string EasingId,
        string Mode,
        double Exponent,
        double Springiness,
        double Oscillations,
        double Bounces,
        double Bounciness)
    {
        public double Ease(double progress) => ConstantEasing?.Ease(progress) ?? LyricEasingFactory.Evaluate(
            EasingId, Mode, progress, Exponent, Springiness, Oscillations, Bounces, Bounciness);
    }
}
