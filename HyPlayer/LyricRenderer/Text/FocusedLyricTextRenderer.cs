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
using System.Linq;
using System.Numerics;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public sealed class FocusedLyricTextRenderer
{
    private static readonly ConcurrentDictionary<string, byte> ReportedOperationFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<FocusedTransitionKey, ScalarTransitionState> _scalarTransitions = [];
    private readonly Dictionary<FocusedTransitionKey, ColorTransitionState> _colorTransitions = [];

    public void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        LyricExpressionLine line,
        LyricExpressionFrame expressionFrame)
    {
        var reveal = profile.Operations.FirstOrDefault(operation =>
            operation.Definition.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal);
        var revealOptions = RevealOptions.From(reveal?.Definition);
        DrawLayer(session, layout.LyricGlyphClusters, LyricTextLayer.Lyric, layout, renderContext,
            profile, revealOptions, line, expressionFrame);
        if (renderContext.EnableTransliteration)
            DrawLayer(session, layout.TransliterationGlyphClusters, LyricTextLayer.Transliteration, layout,
                renderContext, profile, revealOptions, line, expressionFrame);
        if (renderContext.EnableTranslation)
            DrawLayer(session, layout.TranslationGlyphClusters, LyricTextLayer.Translation, layout,
                renderContext, profile, revealOptions, line, expressionFrame);
    }

    private void DrawLayer(
        CanvasDrawingSession session,
        IReadOnlyList<LyricGlyphCluster> clusters,
        LyricTextLayer layer,
        LyricTextLayoutSnapshot layout,
        RenderContext renderContext,
        CompiledFocusedTextEffectProfile profile,
        RevealOptions revealOptions,
        LyricExpressionLine line,
        LyricExpressionFrame frame)
    {
        foreach (var cluster in clusters)
        {
            foreach (var contribution in CreateContributions(cluster, layer, layout, renderContext.CurrentLyricTime, revealOptions, line))
                DrawContribution(session, contribution, layout, renderContext, profile, line, frame);
        }
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
        using var resources = new LyricRenderFrameResourceScope();
        var initial = resources.Track(new CanvasCommandList(session));
        using (var drawing = initial.CreateDrawingSession())
        using (var brush = new CanvasSolidColorBrush(drawing, layout.FocusingColor))
            GlyphRunDrawHelper.DrawCluster(drawing, brush, LyricGlyphDrawState.FromCluster(contribution.Cluster, layout.FocusingColor));

        var image = (ICanvasImage)initial;
        var geometryTransform = Matrix3x2.Identity;
        var currentOrigin = contribution.Cluster.BaseState.Origin;
        var revealProgress = contribution.WordProgress;

        foreach (var operation in profile.Operations)
        {
            var isReveal = operation.Definition.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal;
            if (!isReveal && !operation.Targets.Contains(contribution.Target)) continue;
            var input = image;
            var inputTransform = geometryTransform;
            var inputOrigin = currentOrigin;
            try
            {
                var scopes = CreateScopes(contribution, line, revealProgress, 0);
                switch (operation.Definition.TypeId)
                {
                    case FocusedTextBuiltInOperationTypes.HighlightReveal:
                        image = ApplyReveal(operation, contribution, input, resources, session, line, frame,
                            scopes, geometryTransform, out revealProgress);
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
                        image = ApplyTransform3D(operation, contribution, input, resources, line, frame, scopes, currentOrigin);
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
                        image = ApplyLift(operation, contribution, input, resources, layout, renderContext.CurrentLyricTime,
                            line, frame, revealProgress, ref geometryTransform, ref currentOrigin);
                        break;
                    case FocusedTextBuiltInOperationTypes.DrawScript when operation.DrawScript is not null:
                        image = ApplyScript(operation, contribution, input, resources, session, line, frame, scopes, currentOrigin);
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

    private ICanvasImage ApplyReveal(
        CompiledFocusedTextOperation operation,
        Contribution contribution,
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
        if (contribution.State is not (FocusedTargetState.CurrentHighlighted or FocusedTargetState.CurrentPending))
            return input;

        var offset = Scalar(operation, contribution, "revealTimeOffsetMs", line, frame, scopes, 0);
        var adjustedWordProgress = GetRevealWordProgress(
            frame.CurrentTimeMs,
            contribution.WordStart,
            contribution.WordEnd,
            offset);
        var options = RevealOptions.From(operation.Definition);
        revealProgress = FocusedTextProgress.GetRevealProgress(
            options.Mode,
            adjustedWordProgress,
            contribution.Cluster.TokenClusterIndex,
            contribution.Cluster.TokenClusterCount);
        var highlighted = contribution.State == FocusedTargetState.CurrentHighlighted;

        if (options.Mode != HighlightRevealMode.RectangleClip)
        {
            var opacity = highlighted ? revealProgress : 1 - revealProgress;
            var effect = resources.Track(new OpacityEffect { Source = input, Opacity = opacity });
            return effect;
        }

        var feather = Scalar(operation, contribution, "featherDip", line, frame,
            scopes with { Glyph = scopes.Glyph with { RevealProgress = revealProgress } }, 0);
        return ApplyRectangleMask(input, resources, session, contribution.Cluster, geometryTransform,
            revealProgress, highlighted, feather);
    }

    private static ICanvasImage ApplyRectangleMask(
        ICanvasImage input,
        LyricRenderFrameResourceScope resources,
        CanvasDrawingSession session,
        LyricGlyphCluster cluster,
        Matrix3x2 geometryTransform,
        float reveal,
        bool highlighted,
        float feather)
    {
        var rtl = (cluster.BaseState.BidiLevel & 1) != 0;
        var localReveal = rtl ? 1 - reveal : reveal;
        var left = cluster.VisualLeft;
        var top = cluster.VisualTop;
        var width = Math.Max(cluster.VisualWidth, 0.001f);
        var height = Math.Max(cluster.VisualHeight, 0.001f);

        if (feather <= 0)
        {
            var boundary = left + width * localReveal;
            var clipLeft = highlighted == rtl ? boundary : left;
            var clipRight = highlighted == rtl ? left + width : boundary;
            if (clipRight <= clipLeft)
                return resources.Track(new OpacityEffect { Source = input, Opacity = 0 });
            var hardMask = resources.Track(new CanvasCommandList(session));
            using (var maskSession = hardMask.CreateDrawingSession())
                maskSession.FillRectangle(clipLeft, top, clipRight - clipLeft, height, Colors.White);
            ICanvasImage transformedMask = hardMask;
            if (geometryTransform != Matrix3x2.Identity)
                transformedMask = resources.Track(new Transform2DEffect
                {
                    Source = hardMask,
                    TransformMatrix = geometryTransform
                });
            return resources.Track(new AlphaMaskEffect
            {
                Source = input,
                AlphaMask = transformedMask
            });
        }

        var normalizedFeather = feather / width;
        var boundaryProgress = Math.Clamp(localReveal, 0, 1);
        var first = Math.Clamp(boundaryProgress - normalizedFeather / 2, 0, 1);
        var second = Math.Clamp(boundaryProgress + normalizedFeather / 2, 0, 1);
        var opaqueAtStart = highlighted != rtl;
        var startAlpha = opaqueAtStart ? (byte)255 : (byte)0;
        var endAlpha = opaqueAtStart ? (byte)0 : (byte)255;
        var mask = resources.Track(new CanvasCommandList(session));
        using (var maskSession = mask.CreateDrawingSession())
        using (var brush = new CanvasLinearGradientBrush(maskSession,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(startAlpha, 255, 255, 255) },
            new CanvasGradientStop { Position = first, Color = Color.FromArgb(startAlpha, 255, 255, 255) },
            new CanvasGradientStop { Position = second, Color = Color.FromArgb(endAlpha, 255, 255, 255) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(endAlpha, 255, 255, 255) }
        ])
        {
            StartPoint = new Vector2(left, top),
            EndPoint = new Vector2(left + width, top)
        })
            maskSession.FillRectangle(left, top, width, height, brush);

        ICanvasImage alphaMask = mask;
        if (geometryTransform != Matrix3x2.Identity)
            alphaMask = resources.Track(new Transform2DEffect { Source = mask, TransformMatrix = geometryTransform });
        return resources.Track(new AlphaMaskEffect { Source = input, AlphaMask = alphaMask });
    }

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
        var word = ResolveLiftWord(operation.Definition, contribution, layout, line, timeMs);
        if (word is null) return input;
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
        var easingArguments = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["exponent"] = Scalar(operation, contribution, "exponent", line, frame, baseScopes, 2),
            ["springiness"] = Scalar(operation, contribution, "springiness", line, frame, baseScopes, 3),
            ["oscillations"] = Scalar(operation, contribution, "oscillations", line, frame, baseScopes, 3),
            ["bounces"] = Scalar(operation, contribution, "bounces", line, frame, baseScopes, 2),
            ["bounciness"] = Scalar(operation, contribution, "bounciness", line, frame, baseScopes, 2)
        };
        var easing = LyricEasingFactory.Create(
            operation.Definition.Options.GetValueOrDefault("easingId", "linear"),
            operation.Definition.Options.GetValueOrDefault("easingMode", "in"),
            easingArguments);
        var liftProgress = (float)easing.Ease(local);
        var scopes = baseScopes with { Glyph = baseScopes.Glyph with { LiftProgress = liftProgress } };
        var height = Scalar(operation, contribution, "height", line, frame, scopes, 3);
        var matrix = Matrix3x2.CreateTranslation(0, -height * liftProgress);
        geometryTransform *= matrix;
        currentOrigin = Vector2.Transform(currentOrigin, matrix);
        return resources.Track(new Transform2DEffect { Source = input, TransformMatrix = matrix });
    }

    private static ICanvasImage ApplyScript(
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
                    command.Execute(context, line, frame, scopes.Text, scopes.Word, scopes.Glyph);
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
        if (!operation.Scalars.TryGetValue(key, out var parameter)) return fallback;
        var value = parameter.Expression(line, frame, scopes.Text, scopes.Word, scopes.Glyph, LyricExpressionFunctions.Instance);
        if (!float.IsFinite(value)) throw new InvalidOperationException("表达式返回了 NaN 或 Infinity。");
        if (parameter.Minimum is { } minimum) value = Math.Max(value, minimum);
        if (parameter.Maximum is { } maximum) value = Math.Min(value, maximum);
        if (parameter.Transition is null) return value;
        var keyValue = new FocusedTransitionKey(operation.Definition.InstanceId, key, contribution.Target,
            contribution.Cluster.LayerClusterIndex);
        if (!_scalarTransitions.TryGetValue(keyValue, out var state))
            _scalarTransitions[keyValue] = state = new ScalarTransitionState();
        return state.Animate(value, parameter.Transition, line, frame, scopes);
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
        var value = parameter.Expression(line, frame, scopes.Text, scopes.Word, scopes.Glyph, LyricExpressionFunctions.Instance);
        if (parameter.Transition is null) return value;
        var keyValue = new FocusedTransitionKey(operation.Definition.InstanceId, key, contribution.Target,
            contribution.Cluster.LayerClusterIndex);
        if (!_colorTransitions.TryGetValue(keyValue, out var state))
            _colorTransitions[keyValue] = state = new ColorTransitionState();
        return state.Animate(value, parameter.Transition, line, frame, scopes);
    }

    private static Contribution[] CreateContributions(
        LyricGlyphCluster cluster,
        LyricTextLayer layer,
        LyricTextLayoutSnapshot layout,
        long timeMs,
        RevealOptions options,
        LyricExpressionLine line)
    {
        if (layer == LyricTextLayer.Translation)
            return [new Contribution(cluster, FocusedTextTargets.Translation, FocusedTargetState.Highlighted,
                null, line.StartMs, line.EndMs, line.Progress)];

        var tokens = layout.Tokens;
        var hasRealWords = tokens.Any(token => !token.IsInferred);
        var useWords = hasRealWords || options.UntimedMode == UntimedHighlightMode.InferWords;
        if (layer == LyricTextLayer.Transliteration && options.TransliterationMode == TransliterationProgressMode.WholeLine)
            useWords = false;
        if (!useWords || tokens.Count == 0)
        {
            var highlighted = options.UntimedMode == UntimedHighlightMode.WholeLine ||
                              layer == LyricTextLayer.Transliteration &&
                              options.TransliterationMode == TransliterationProgressMode.WholeLine;
            var state = highlighted ? FocusedTargetState.Highlighted : FocusedTargetState.Unhighlighted;
            return [new Contribution(cluster, Target(layer, state), state, null,
                line.StartMs, line.EndMs, highlighted ? 1 : 0)];
        }

        var tokenIndex = Math.Clamp(cluster.TokenStartIndex, 0, tokens.Count - 1);
        var token = tokens[tokenIndex];
        if (timeMs < token.StartTime)
            return [new Contribution(cluster, Target(layer, FocusedTargetState.Unhighlighted),
                FocusedTargetState.Unhighlighted, token, token.StartTime, token.EndTime, 0)];
        if (timeMs >= token.EndTime)
            return [new Contribution(cluster, Target(layer, FocusedTargetState.Highlighted),
                FocusedTargetState.Highlighted, token, token.StartTime, token.EndTime, 1)];

        var duration = token.EndTime - token.StartTime;
        var progress = duration <= 0 ? 1 : Math.Clamp((timeMs - token.StartTime) / (float)duration, 0, 1);
        return
        [
            new Contribution(cluster, Target(layer, FocusedTargetState.CurrentPending),
                FocusedTargetState.CurrentPending, token, token.StartTime, token.EndTime, progress),
            new Contribution(cluster, Target(layer, FocusedTargetState.CurrentHighlighted),
                FocusedTargetState.CurrentHighlighted, token, token.StartTime, token.EndTime, progress)
        ];
    }

    private static LiftWord? ResolveLiftWord(
        FocusedTextOperationDefinition definition,
        Contribution contribution,
        LyricTextLayoutSnapshot layout,
        LyricExpressionLine line,
        long timeMs)
    {
        var mode = Option(definition, "untimedMode", UntimedLiftMode.DoNotLift);
        var hasRealWords = layout.Tokens.Any(token => !token.IsInferred);
        if (contribution.Cluster.Layer != LyricTextLayer.Translation && hasRealWords)
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
        var inferredTokenIndex = TokenAtSource(inferred, contribution.Cluster.SourceStart);
        if ((uint)inferredTokenIndex >= (uint)inferred.Count) return null;
        var token = inferred[inferredTokenIndex];
        var layerClusters = contribution.Cluster.Layer switch
        {
            LyricTextLayer.Transliteration => layout.TransliterationGlyphClusters,
            LyricTextLayer.Translation => layout.TranslationGlyphClusters,
            _ => layout.LyricGlyphClusters
        };
        var (glyphIndex, glyphCount) = GlyphPositionForToken(
            contribution.Cluster, layerClusters, inferred, inferredTokenIndex);
        return new LiftWord(token.StartTime, token.EndTime, glyphIndex, glyphCount,
            true, inferredTokenIndex, inferred.Count, true, WordProgress(timeMs, token.StartTime, token.EndTime));
    }

    private static int TokenAtSource(IReadOnlyList<LyricTextToken> tokens, int sourceIndex)
    {
        var offset = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            offset += tokens[index].Text.Length;
            if (sourceIndex < offset) return index;
        }
        return tokens.Count - 1;
    }

    private static (int Index, int Count) GlyphPositionForToken(
        LyricGlyphCluster cluster,
        IReadOnlyList<LyricGlyphCluster> layerClusters,
        IReadOnlyList<LyricTextToken> tokens,
        int tokenIndex)
    {
        var start = 0;
        for (var index = 0; index < tokenIndex; index++) start += tokens[index].Text.Length;
        var end = start + tokens[tokenIndex].Text.Length;
        var tokenClusters = layerClusters.Where(item =>
            item.SourceStart >= 0 && item.SourceEnd > start && item.SourceStart < end).ToList();
        var glyphIndex = tokenClusters.IndexOf(cluster);
        return (Math.Max(0, glyphIndex), Math.Max(1, tokenClusters.Count));
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
        return new ExpressionScopes(
            new FocusedTextExpressionText(
                cluster.Layer == LyricTextLayer.Lyric,
                cluster.Layer == LyricTextLayer.Transliteration,
                cluster.Layer == LyricTextLayer.Translation),
            word is null
                ? new FocusedTextExpressionWord(false, -1, 0, line.StartMs, line.EndMs,
                    line.Progress, false)
                : new FocusedTextExpressionWord(word.Exists, word.Index, word.Count,
                    word.Start, word.End, word.Progress, word.IsInferred),
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

    private sealed record Contribution(
        LyricGlyphCluster Cluster,
        string Target,
        FocusedTargetState State,
        LyricTextToken? Word,
        long WordStart,
        long WordEnd,
        float WordProgress);

    private sealed record LiftWord(
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
            LyricExpressionLine line, LyricExpressionFrame frame, ExpressionScopes scopes)
        {
            if (!_initialized)
            {
                _initialized = true;
                _start = _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes);
                return target;
            }
            if (target != _target)
            {
                _start = Lerp(_start, _target, Progress(frame.CurrentTimeMs));
                _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes);
            }
            return Lerp(_start, _target, Progress(frame.CurrentTimeMs));
        }

        private double Progress(long time) => _snapshot.Duration <= 0
            ? 1
            : _snapshot.Easing.Ease(Math.Clamp((time - _startTime) / _snapshot.Duration, 0, 1));
    }

    private sealed class ColorTransitionState
    {
        private bool _initialized;
        private LyricColorValue _start;
        private LyricColorValue _target;
        private long _startTime;
        private TransitionSnapshot _snapshot;

        public LyricColorValue Animate(LyricColorValue target, CompiledFocusedTransition transition,
            LyricExpressionLine line, LyricExpressionFrame frame, ExpressionScopes scopes)
        {
            if (!_initialized)
            {
                _initialized = true;
                _start = _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes);
                return target;
            }
            if (target != _target)
            {
                _start = Lerp(_start, _target, Progress(frame.CurrentTimeMs));
                _target = target;
                _startTime = frame.CurrentTimeMs;
                _snapshot = Snapshot(transition, line, frame, scopes);
            }
            return Lerp(_start, _target, Progress(frame.CurrentTimeMs));
        }

        private double Progress(long time) => _snapshot.Duration <= 0
            ? 1
            : _snapshot.Easing.Ease(Math.Clamp((time - _startTime) / _snapshot.Duration, 0, 1));
    }

    private static TransitionSnapshot Snapshot(
        CompiledFocusedTransition transition,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        ExpressionScopes scopes)
    {
        var fx = LyricExpressionFunctions.Instance;
        var duration = transition.Duration(line, frame, scopes.Text, scopes.Word, scopes.Glyph, fx);
        var arguments = transition.Arguments.ToDictionary(
            pair => pair.Key,
            pair => (double)pair.Value(line, frame, scopes.Text, scopes.Word, scopes.Glyph, fx),
            StringComparer.OrdinalIgnoreCase);
        return new TransitionSnapshot(duration,
            LyricEasingFactory.Create(transition.EasingId, transition.Mode, arguments));
    }

    private static float Lerp(float start, float end, double progress) =>
        (float)(start + (end - start) * progress);

    private static LyricColorValue Lerp(LyricColorValue start, LyricColorValue end, double progress) => new(
        Channel(start.A, end.A, progress), Channel(start.R, end.R, progress),
        Channel(start.G, end.G, progress), Channel(start.B, end.B, progress));

    private static byte Channel(byte start, byte end, double progress) =>
        (byte)Math.Clamp(Math.Round(start + (end - start) * progress), byte.MinValue, byte.MaxValue);

    private readonly record struct TransitionSnapshot(
        double Duration,
        HyPlayer.LyricRenderer.Animator.EaseFunctionBase Easing);
}
