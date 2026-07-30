#nullable enable

using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Pipeline;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public sealed class FocusedLyricTextRenderer
{
    private static readonly ConcurrentDictionary<string, byte> ReportedOperationFailures = new(StringComparer.Ordinal);

    public void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame)
    {
        using var brush = new CanvasSolidColorBrush(session, layout.FocusingColor);
        DrawLayer(session, brush, layout.LyricGlyphClusters, LyricTextLayer.Lyric,
            layout, frame, renderContext, profile, line, expressionFrame);
        if (renderContext.EnableTransliteration)
        {
            DrawLayer(session, brush, layout.TransliterationGlyphClusters, LyricTextLayer.Transliteration,
                layout, frame, renderContext, profile, line, expressionFrame);
        }
        if (renderContext.EnableTranslation)
        {
            DrawLayer(session, brush, layout.TranslationGlyphClusters, LyricTextLayer.Translation,
                layout, frame, renderContext, profile, line, expressionFrame);
        }
    }

    private static void DrawLayer(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        IReadOnlyList<LyricGlyphCluster> clusters,
        LyricTextLayer layer,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame)
    {
        for (var index = 0; index < clusters.Count; index++)
        {
            var cluster = clusters[index];
            if (layer == LyricTextLayer.Translation)
            {
                DrawContribution(session, brush, cluster, FocusedTextTargets.Translation, 1, 1, null,
                    layout, frame, renderContext, profile, line, expressionFrame, line.Progress);
                continue;
            }

            if (layer == LyricTextLayer.Transliteration &&
                profile.Definition.TransliterationMode == TransliterationProgressMode.WholeLine)
            {
                DrawContribution(session, brush, cluster, Target(layer, FocusedTargetState.Highlighted), 1, 1, null,
                    layout, frame, renderContext, profile, line, expressionFrame, 1);
                continue;
            }

            var tokenIndex = cluster.TokenStartIndex;
            if (layout.Tokens.Count == 0)
            {
                DrawContribution(session, brush, cluster, Target(layer, FocusedTargetState.Highlighted), 1, 1, null,
                    layout, frame, renderContext, profile, line, expressionFrame, frame.LineProgress);
                continue;
            }

            if (frame.CurrentTokenIndex < 0 || tokenIndex > frame.CurrentTokenIndex)
            {
                DrawContribution(session, brush, cluster, Target(layer, FocusedTargetState.Unhighlighted), 1, 0, null,
                    layout, frame, renderContext, profile, line, expressionFrame, 0);
                continue;
            }
            if (tokenIndex < frame.CurrentTokenIndex || frame.CurrentTokenProgress >= 1)
            {
                DrawContribution(session, brush, cluster, Target(layer, FocusedTargetState.Highlighted), 1, 1, null,
                    layout, frame, renderContext, profile, line, expressionFrame, 1);
                continue;
            }

            var reveal = FocusedTextProgress.GetRevealProgress(
                profile.Definition.HighlightRevealMode,
                frame.CurrentTokenProgress,
                cluster.TokenClusterIndex,
                cluster.TokenClusterCount);
            if (profile.Definition.HighlightRevealMode == HighlightRevealMode.RectangleClip)
            {
                DrawCurrentRectangleContributions(session, brush, cluster, layer, reveal,
                    layout, frame, renderContext, profile, line, expressionFrame);
            }
            else
            {
                if (reveal < 1)
                    DrawContribution(session, brush, cluster, Target(layer, FocusedTargetState.CurrentPending),
                        1 - reveal, reveal, null, layout, frame, renderContext, profile, line, expressionFrame, frame.CurrentTokenProgress);
                if (reveal > 0)
                    DrawContribution(session, brush, cluster, Target(layer, FocusedTargetState.CurrentHighlighted),
                        reveal, reveal, null, layout, frame, renderContext, profile, line, expressionFrame, frame.CurrentTokenProgress);
            }
        }
    }

    private static void DrawCurrentRectangleContributions(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphCluster cluster,
        LyricTextLayer layer,
        float reveal,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame)
    {
        if (reveal < 1)
        {
            var state = CreateState(cluster, layout.FocusingColor);
            var scripts = new List<FocusedScriptInvocation>();
            ApplyOperations(profile, Target(layer, FocusedTargetState.CurrentPending), cluster, layout, frame, line, expressionFrame,
                layer, frame.CurrentTokenProgress, reveal, ref state, scripts);
            var clip = RevealClip(cluster, state, reveal, highlighted: false);
            DrawScripts(session, state, clip, scripts, FocusedDrawScriptPlacement.BehindGlyph);
            GlyphRunDrawHelper.DrawCluster(session, brush, state, clip);
            DrawScripts(session, state, clip, scripts, FocusedDrawScriptPlacement.AboveGlyph);
        }
        if (reveal > 0)
        {
            var state = CreateState(cluster, layout.FocusingColor);
            var scripts = new List<FocusedScriptInvocation>();
            ApplyOperations(profile, Target(layer, FocusedTargetState.CurrentHighlighted), cluster, layout, frame, line, expressionFrame,
                layer, frame.CurrentTokenProgress, reveal, ref state, scripts);
            var clip = RevealClip(cluster, state, reveal, highlighted: true);
            DrawScripts(session, state, clip, scripts, FocusedDrawScriptPlacement.BehindGlyph);
            GlyphRunDrawHelper.DrawCluster(session, brush, state, clip);
            DrawScripts(session, state, clip, scripts, FocusedDrawScriptPlacement.AboveGlyph);
        }
    }

    private static void DrawContribution(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphCluster cluster,
        string target,
        float contributionOpacity,
        float revealProgress,
        Rect? clip,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame,
        float wordProgress)
    {
        var state = CreateState(cluster, layout.FocusingColor);
        state.Opacity *= contributionOpacity;
        var scripts = new List<FocusedScriptInvocation>();
        ApplyOperations(profile, target, cluster, layout, frame, line, expressionFrame, cluster.Layer, wordProgress,
            revealProgress, ref state, scripts);
        DrawScripts(session, state, clip, scripts, FocusedDrawScriptPlacement.BehindGlyph);
        GlyphRunDrawHelper.DrawCluster(session, brush, state, clip);
        DrawScripts(session, state, clip, scripts, FocusedDrawScriptPlacement.AboveGlyph);
    }

    private static LyricGlyphDrawState CreateState(LyricGlyphCluster cluster, Color color) =>
        LyricGlyphDrawState.FromCluster(cluster, color);

    private static void ApplyOperations(
        CompiledFocusedTextEffectProfile profile,
        string target,
        LyricGlyphCluster cluster,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame,
        LyricTextLayer layer,
        float wordProgress,
        float revealProgress,
        ref LyricGlyphDrawState state,
        ICollection<FocusedScriptInvocation> scripts)
    {
        var token = (uint)cluster.TokenStartIndex < (uint)layout.Tokens.Count
            ? layout.Tokens[cluster.TokenStartIndex]
            : null;
        var wordExists = token is not null;
        var wordIndex = wordExists ? cluster.TokenStartIndex : -1;
        var wordCount = layout.Tokens.Count;
        var wordStart = token?.StartTime ?? line.StartMs;
        var wordEnd = token?.EndTime ?? line.EndMs;
        var wordDuration = Math.Max(wordEnd - wordStart, 0);
        var glyphIndexInWord = wordExists ? cluster.TokenClusterIndex : cluster.LayerClusterIndex;
        var glyphCountInWord = wordExists ? cluster.TokenClusterCount : cluster.LayerClusterCount;
        var textScope = new FocusedTextExpressionText(
            layer == LyricTextLayer.Lyric,
            layer == LyricTextLayer.Transliteration,
            layer == LyricTextLayer.Translation);
        var wordScope = new FocusedTextExpressionWord(
            wordExists,
            wordIndex,
            wordCount,
            wordStart,
            wordEnd,
            wordProgress,
            token?.IsInferred == true);
        var currentMotionProgress = FocusedTextProgress.GetMotionProgress(
            wordProgress,
            wordDuration,
            glyphIndexInWord,
            glyphCountInWord,
            0,
            1000,
            GlyphLiftMotion.Hold);

        foreach (var operation in profile.Operations)
        {
            if (!operation.Targets.Contains(target)) continue;
            var operationInputState = state;
            var operationInputMotionProgress = currentMotionProgress;
            try
            {
                var glyphScope = new FocusedTextExpressionGlyph(
                    cluster.LayerClusterIndex,
                    cluster.LayerClusterCount,
                    glyphIndexInWord,
                    glyphCountInWord,
                    revealProgress,
                    currentMotionProgress);
                var type = operation.Definition.TypeId;
                if (type == FocusedTextBuiltInOperationTypes.Color)
                {
                    state.Color = ToColor(Color(operation, "color", line, expressionFrame, textScope, wordScope, glyphScope,
                        new LyricColorValue(state.Color.A, state.Color.R, state.Color.G, state.Color.B)));
                }
                else if (type == FocusedTextBuiltInOperationTypes.Opacity)
                {
                    state.Opacity *= Scalar(operation, "opacity", line, expressionFrame, textScope, wordScope, glyphScope, 1, 0, 1);
                }
                else if (type == FocusedTextBuiltInOperationTypes.Transform2D)
                {
                state.Origin += new Vector2(
                    Scalar(operation, "x", line, expressionFrame, textScope, wordScope, glyphScope, 0),
                    Scalar(operation, "y", line, expressionFrame, textScope, wordScope, glyphScope, 0));
                state.ScaleX *= Scalar(operation, "scaleX", line, expressionFrame, textScope, wordScope, glyphScope, 1, -10, 10);
                state.ScaleY *= Scalar(operation, "scaleY", line, expressionFrame, textScope, wordScope, glyphScope, 1, -10, 10);
                state.Rotation += Scalar(operation, "rotation", line, expressionFrame, textScope, wordScope, glyphScope, 0);
                }
                else if (type == FocusedTextBuiltInOperationTypes.Transform3D)
                {
                state.RotationX += Scalar(operation, "angleX", line, expressionFrame, textScope, wordScope, glyphScope, 0);
                state.RotationY += Scalar(operation, "angleY", line, expressionFrame, textScope, wordScope, glyphScope, 0);
                state.Rotation += Scalar(operation, "angleZ", line, expressionFrame, textScope, wordScope, glyphScope, 0);
                state.PerspectiveDepth = Scalar(
                    operation, "depth", line, expressionFrame, textScope, wordScope, glyphScope, 3000, 1, 100000);
                }
                else if (type == FocusedTextBuiltInOperationTypes.GaussianBlur)
                {
                state.BlurRadius += Scalar(operation, "amount", line, expressionFrame, textScope, wordScope, glyphScope, 0, 0, 250);
                }
                else if (type == FocusedTextBuiltInOperationTypes.Glow)
                {
                state.GlowRadius = Scalar(operation, "blur", line, expressionFrame, textScope, wordScope, glyphScope, 4, 0, 250);
                state.GlowOpacity = Scalar(operation, "opacity", line, expressionFrame, textScope, wordScope, glyphScope, 0.4f, 0, 1);
                state.GlowColor = ToColor(Color(operation, "color", line, expressionFrame, textScope, wordScope, glyphScope,
                    new LyricColorValue(state.Color.A, state.Color.R, state.Color.G, state.Color.B)));
                }
                else if (type == FocusedTextBuiltInOperationTypes.Stroke)
                {
                state.StrokeWidth = Scalar(operation, "width", line, expressionFrame, textScope, wordScope, glyphScope, 1, 0, 32);
                state.StrokeColor = ToColor(Color(operation, "color", line, expressionFrame, textScope, wordScope, glyphScope,
                    new LyricColorValue(state.Color.A, state.Color.R, state.Color.G, state.Color.B)));
                }
                else if (type == FocusedTextBuiltInOperationTypes.Shadow)
                {
                state.ShadowOffset = new Vector2(
                    Scalar(operation, "x", line, expressionFrame, textScope, wordScope, glyphScope, 0),
                    Scalar(operation, "y", line, expressionFrame, textScope, wordScope, glyphScope, 2));
                state.ShadowBlur = Scalar(operation, "blur", line, expressionFrame, textScope, wordScope, glyphScope, 4, 0, 250);
                state.ShadowOpacity = Scalar(operation, "opacity", line, expressionFrame, textScope, wordScope, glyphScope, 0.5f, 0, 1);
                state.ShadowColor = ToColor(Color(operation, "color", line, expressionFrame, textScope, wordScope, glyphScope,
                    new LyricColorValue(255, 0, 0, 0)));
                }
                else if (type == FocusedTextBuiltInOperationTypes.GlyphLift)
                {
                var overlap = Scalar(operation, "overlap", line, expressionFrame, textScope, wordScope, glyphScope, 0, 0, 1);
                var threshold = Scalar(operation, "wholeWordThresholdMs", line, expressionFrame, textScope, wordScope, glyphScope, 1000, 0, 60000);
                var motion = operation.Definition.Options.TryGetValue("motion", out var motionName) &&
                             motionName.Equals("Pulse", StringComparison.OrdinalIgnoreCase)
                    ? GlyphLiftMotion.Pulse
                    : GlyphLiftMotion.Hold;
                var motionProgress = FocusedTextProgress.GetMotionProgress(
                    wordProgress, wordDuration, glyphIndexInWord, glyphCountInWord,
                    overlap, threshold, motion);
                currentMotionProgress = motionProgress;
                glyphScope = glyphScope with { MotionProgress = motionProgress };
                var height = Scalar(operation, "height", line, expressionFrame, textScope, wordScope, glyphScope, 3);
                state.Origin.Y -= height * motionProgress;
                }
                else if (type == FocusedTextBuiltInOperationTypes.DrawScript && operation.DrawScript is not null)
                {
                scripts.Add(new FocusedScriptInvocation(
                    operation.Definition.InstanceId,
                    operation.DrawScript,
                    line,
                    expressionFrame,
                    textScope,
                    wordScope,
                    glyphScope));
                }
            }
            catch (Exception exception)
            {
                state = operationInputState;
                currentMotionProgress = operationInputMotionProgress;
                if (ReportedOperationFailures.TryAdd(operation.Definition.InstanceId, 0))
                    Debug.WriteLine($"Focused lyric operation {operation.Definition.InstanceId} failed: {exception}");
            }
        }
    }

    private static void DrawScripts(
        CanvasDrawingSession session,
        LyricGlyphDrawState state,
        Rect? clip,
        IReadOnlyCollection<FocusedScriptInvocation> scripts,
        FocusedDrawScriptPlacement placement)
    {
        foreach (var invocation in scripts)
        {
            if (invocation.Script.Placement != placement) continue;
            var originalTransform = session.Transform;
            try
            {
                using var layer = clip is { } clipRect ? session.CreateLayer(1, clipRect) : null;
                session.Transform =
                    Matrix3x2.CreateScale(state.Scale * state.ScaleX, state.Scale * state.ScaleY) *
                    Matrix3x2.CreateRotation(MathF.PI * state.Rotation / 180f) *
                    Matrix3x2.CreateTranslation(state.Origin) *
                    originalTransform;
                var context = new LyricDrawExecutionContext(session);
                foreach (var command in invocation.Script.Commands)
                {
                    command.Execute(
                        context,
                        invocation.Line,
                        invocation.Frame,
                        invocation.Text,
                        invocation.Word,
                        invocation.Glyph);
                }
                context.EnsureBalanced();
            }
            catch (Exception exception)
            {
                if (ReportedOperationFailures.TryAdd($"script:{invocation.InstanceId}", 0))
                    Debug.WriteLine($"Focused lyric draw script {invocation.InstanceId} failed: {exception}");
            }
            finally
            {
                session.Transform = originalTransform;
            }
        }
    }

    private static float Scalar(
        CompiledFocusedTextOperation operation,
        string key,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph,
        float fallback,
        float? minimum = null,
        float? maximum = null)
    {
        if (!operation.Scalars.TryGetValue(key, out var expression)) return fallback;
        var value = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
        if (!float.IsFinite(value)) return fallback;
        if (minimum is { } min) value = Math.Max(value, min);
        if (maximum is { } max) value = Math.Min(value, max);
        return value;
    }

    private static LyricColorValue Color(
        CompiledFocusedTextOperation operation,
        string key,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph,
        LyricColorValue fallback) => operation.Colors.TryGetValue(key, out var expression)
        ? expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance)
        : fallback;

    private static Color ToColor(LyricColorValue color) =>
        Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

    private static Rect? RevealClip(
        LyricGlyphCluster cluster,
        LyricGlyphDrawState state,
        float reveal,
        bool highlighted)
    {
        var offset = state.Origin - cluster.BaseState.Origin;
        var left = cluster.VisualLeft + offset.X;
        var top = cluster.VisualTop + offset.Y;
        var rtl = (cluster.BaseState.BidiLevel & 1) != 0;
        var clip = FocusedRevealClipCalculator.GetContributionClip(
            left,
            top,
            cluster.VisualWidth,
            cluster.VisualHeight,
            reveal,
            highlighted,
            rtl,
            GetEffectOutsets(state));
        return clip is { } value
            ? new Rect(value.Left, value.Top, value.Width, value.Height)
            : null;
    }

    private static FocusedEffectOutsets GetEffectOutsets(LyricGlyphDrawState state)
    {
        // Win2D's Gaussian blur amount is sigma. Three sigma contains virtually
        // all visible blur while keeping the contribution clip finite.
        var blurOutset = Math.Max(state.BlurRadius, 0) * 3;
        var glowOutset = state.GlowOpacity > 0.001f
            ? Math.Max(state.GlowRadius, 0) * 3
            : 0;
        var strokeOutset = Math.Clamp(state.StrokeWidth, 0, 8);
        var common = Math.Max(Math.Max(blurOutset, glowOutset), strokeOutset);
        var left = common;
        var top = common;
        var right = common;
        var bottom = common;

        if (state.ShadowOpacity > 0.001f)
        {
            var shadowBlurOutset = Math.Max(state.ShadowBlur, 0) * 3;
            left = Math.Max(left, shadowBlurOutset - state.ShadowOffset.X);
            top = Math.Max(top, shadowBlurOutset - state.ShadowOffset.Y);
            right = Math.Max(right, shadowBlurOutset + state.ShadowOffset.X);
            bottom = Math.Max(bottom, shadowBlurOutset + state.ShadowOffset.Y);
        }

        return new FocusedEffectOutsets(left, top, right, bottom);
    }

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

    private sealed record FocusedScriptInvocation(
        string InstanceId,
        CompiledFocusedDrawScript Script,
        LyricExpressionLine Line,
        LyricExpressionFrame Frame,
        FocusedTextExpressionText Text,
        FocusedTextExpressionWord Word,
        FocusedTextExpressionGlyph Glyph);
}
