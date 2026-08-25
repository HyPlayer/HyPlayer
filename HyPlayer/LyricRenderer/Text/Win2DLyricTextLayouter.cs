#nullable enable

using HyPlayer.LyricEffects.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace HyPlayer.LyricRenderer.Text;

public sealed class Win2DLyricTextLayouter : ILyricTextLayouter
{
    public LyricTextLayoutSnapshot CreateLayout(LyricTextLayoutRequest request)
    {
        var tokens = request.Tokens.Count > 0
            ? request.Tokens
            : ShouldInferWords(request.Context.EffectProfile?.FocusedText.Definition)
                ? InferredLyricWordTokenizer.Infer(
                    request.Text,
                    request.Transliteration,
                    request.LineStartTime,
                    request.LineEndTime)
                : [];
        using var textFormat = CreateTextFormat(
            request.LyricFontSize,
            request.Alignment,
            request.FontFamily,
            FontWeights.SemiBold,
            CanvasWordWrapping.WholeWord);

        var hasTokenTransliteration = tokens.Any(t => !string.IsNullOrEmpty(t.Transliteration));
        var transliterationActual = hasTokenTransliteration
            ? string.Join("", tokens.Select(t => t.Transliteration ?? string.Empty))
            : request.Transliteration;

        CanvasTextLayout? transliterationLayout = null;
        CanvasTextLayout? translationLayout = null;
        var additionalHeight = 0.0f;
        var requestedWidth = Math.Clamp(request.Context.ItemWidth - request.TextPadding, 0, int.MaxValue);

        if ((!string.IsNullOrWhiteSpace(transliterationActual) || !string.IsNullOrWhiteSpace(request.Translation) || hasTokenTransliteration) &&
            !string.IsNullOrWhiteSpace(transliterationActual) &&
            request.Context.EnableTransliteration)
        {
            using var transliterationFormat = CreateTextFormat(
                request.TransliterationFontSize,
                request.Alignment,
                request.FontFamily,
                FontWeights.Normal,
                CanvasWordWrapping.Wrap);
            transliterationLayout = new CanvasTextLayout(
                request.Session,
                transliterationActual,
                transliterationFormat,
                requestedWidth,
                request.CanvasHeight);
            additionalHeight += 10;
        }

        if (!string.IsNullOrWhiteSpace(request.Translation) && request.Context.EnableTranslation)
        {
            using var translationFormat = CreateTextFormat(
                request.TranslationFontSize,
                request.Alignment,
                request.FontFamily,
                FontWeights.Normal,
                CanvasWordWrapping.Wrap);
            translationLayout = new CanvasTextLayout(
                request.Session,
                request.Translation.TrimEnd(),
                translationFormat,
                Math.Clamp(request.Context.ItemWidth - request.TextPadding, 10, int.MaxValue),
                request.CanvasHeight);
        }

        additionalHeight += (float)(transliterationLayout?.LayoutBounds.Height ?? 0f);
        additionalHeight += (float)(translationLayout?.LayoutBounds.Height ?? 0f);

        var actualText = tokens.Count > 0
            ? string.Join("", tokens.Select(t => t.Text))
            : request.Text;
        var wrappedText = WrapText(request.Session, actualText, textFormat, requestedWidth, request.CanvasHeight);
        var textLayout = new CanvasTextLayout(request.Session, wrappedText, textFormat, requestedWidth, request.CanvasHeight);
        var lyricSourceIndexMap = CreateSourceIndexMap(wrappedText, actualText);
        var lyricTokenIndexMap = CreateTokenIndexMap(tokens, t => t.Text);
        var transliterationSourceIndexMap = transliterationActual is not null
            ? CreateSourceIndexMap(transliterationActual, transliterationActual)
            : [];
        var transliterationTokenIndexMap = hasTokenTransliteration
            ? CreateTokenIndexMap(tokens, t => t.Transliteration ?? string.Empty)
            : [];
        var inferredTransliterationTokens = InferredLyricWordTokenizer.Infer(
            transliterationActual ?? string.Empty, null, request.LineStartTime, request.LineEndTime);
        var inferredTranslationTokens = InferredLyricWordTokenizer.Infer(
            request.Translation?.TrimEnd() ?? string.Empty, null, request.LineStartTime, request.LineEndTime);

        var renderStartX = GetContentLeft(textLayout, transliterationLayout, translationLayout);
        var renderEndX = GetContentRight(textLayout, transliterationLayout, translationLayout);
        var drawOffsetX = -renderStartX + request.TextPadding;
        var scalingCenterX = GetScalingCenterX(textLayout, renderStartX, request.TextPadding, request.Alignment);
        var drawingOffsetY = request.LyricFontSize / 8f;
        var renderingHeight = (float)textLayout.LayoutBounds.Height + drawingOffsetY + additionalHeight;
        var renderingWidth = Math.Max(1, renderEndX - renderStartX + request.TextPadding * 2);
        // 音译必须始终进入动态 Glyph 层，否则 WholeLine 模式会绕过目标选择和聚焦特效链。
        var useDynamicTransliteration = transliterationLayout is not null;

        var staticPersistCache = CreatePersistCache(request.Session, renderingWidth, renderingHeight, request.Context.Dpi, request.Context.Effects.CacheRenderTarget, out var staticSession);
        var defaultTextPersistCache = CreatePersistCache(request.Session, renderingWidth, renderingHeight, request.Context.Dpi, request.Context.Effects.CacheRenderTarget, out var defaultTextSession);
        ICanvasImage? defaultTransliterationPersistCache = null;
        CanvasDrawingSession? transliterationSessionToDispose = null;
        if (useDynamicTransliteration)
        {
            defaultTransliterationPersistCache = CreatePersistCache(
                request.Session,
                renderingWidth,
                renderingHeight,
                request.Context.Dpi,
                request.Context.Effects.CacheRenderTarget,
                out transliterationSessionToDispose);
        }
        ICanvasImage? defaultTranslationPersistCache = null;
        CanvasDrawingSession? translationSessionToDispose = null;
        if (translationLayout is not null)
        {
            defaultTranslationPersistCache = CreatePersistCache(
                request.Session,
                renderingWidth,
                renderingHeight,
                request.Context.Dpi,
                request.Context.Effects.CacheRenderTarget,
                out translationSessionToDispose);
        }
        var sizePixelRect = new Rect(0, 0, renderingWidth, renderingHeight);
        float textRenderActualTop;
        float translationRenderActualTop = 0;
        float transliterationRenderTop = drawingOffsetY;
        IReadOnlyList<LyricGlyphCluster> lyricGlyphClusters;
        IReadOnlyList<LyricGlyphCluster> transliterationGlyphClusters = [];
        IReadOnlyList<LyricGlyphCluster> translationGlyphClusters = [];

        using (staticSession)
        using (defaultTextSession)
        using (transliterationSessionToDispose)
        using (translationSessionToDispose)
        {
            staticSession.Clear(Colors.Transparent);
            defaultTextSession.Clear(Colors.Transparent);
            transliterationSessionToDispose?.Clear(Colors.Transparent);
            translationSessionToDispose?.Clear(Colors.Transparent);
            var actualTop = drawingOffsetY;

            if (transliterationLayout is not null)
            {
                if (useDynamicTransliteration)
                {
                    transliterationSessionToDispose!.DrawTextLayout(transliterationLayout, drawOffsetX, actualTop, request.IdleColor);
                }
                else
                {
                    staticSession.DrawTextLayout(transliterationLayout, drawOffsetX, actualTop, request.IdleColor);
                }

                actualTop += (float)transliterationLayout.LayoutBounds.Height;
            }

            textRenderActualTop = actualTop;
            defaultTextSession.DrawTextLayout(textLayout, drawOffsetX, 0, request.IdleColor);
            actualTop += (float)textLayout.LayoutBounds.Height;

            if (translationLayout is not null)
            {
                translationRenderActualTop = actualTop;
                translationSessionToDispose!.DrawTextLayout(translationLayout, drawOffsetX, 0, request.IdleColor);
            }
            if (translationLayout is not null)
            {
                var translationSource = request.Translation!.TrimEnd();
                translationGlyphClusters = CollectGlyphClusters(
                    translationSessionToDispose!,
                    LyricTextLayer.Translation,
                    translationLayout,
                    drawOffsetX,
                    translationRenderActualTop,
                    CreateSourceIndexMap(translationSource, translationSource),
                    [],
                    request.Context.Dpi,
                    0);
            }

            lyricGlyphClusters = CollectGlyphClusters(
                defaultTextSession,
                LyricTextLayer.Lyric,
                textLayout,
                drawOffsetX,
                textRenderActualTop,
                lyricSourceIndexMap,
                lyricTokenIndexMap,
                request.Context.Dpi,
                tokens.Count);
            RetimeInferredTokens(tokens, lyricGlyphClusters, request.LineStartTime, request.LineEndTime);
            if (useDynamicTransliteration)
            {
                transliterationGlyphClusters = CollectGlyphClusters(
                    transliterationSessionToDispose!,
                    LyricTextLayer.Transliteration,
                    transliterationLayout!,
                    drawOffsetX,
                    transliterationRenderTop,
                    transliterationSourceIndexMap,
                    transliterationTokenIndexMap,
                    request.Context.Dpi,
                    tokens.Count,
                    proportionalTokenWeights: !hasTokenTransliteration
                        ? CreateTokenGlyphWeights(lyricGlyphClusters, tokens.Count)
                        : null);
            }
            RetimeInferredTokens(inferredTransliterationTokens, transliterationGlyphClusters,
                request.LineStartTime, request.LineEndTime);
            RetimeInferredTokens(inferredTranslationTokens, translationGlyphClusters,
                request.LineStartTime, request.LineEndTime);
            MapInferredTokenClusters(inferredTransliterationTokens, transliterationGlyphClusters);
            MapInferredTokenClusters(inferredTranslationTokens, translationGlyphClusters);
        }

        return new LyricTextLayoutSnapshot
        {
            Text = actualText,
            Tokens = tokens.ToArray(),
            HasRealWords = tokens.Any(token => !token.IsInferred),
            InferredTransliterationTokens = inferredTransliterationTokens,
            InferredTranslationTokens = inferredTranslationTokens,
            TextLayout = textLayout,
            TranslationLayout = translationLayout,
            TransliterationLayout = transliterationLayout,
            StaticPersistCache = staticPersistCache,
            DefaultTextPersistCache = defaultTextPersistCache,
            DefaultTransliterationPersistCache = defaultTransliterationPersistCache,
            DefaultTranslationPersistCache = defaultTranslationPersistCache,
            SizePixelRect = sizePixelRect,
            TextRenderActualTop = textRenderActualTop,
            TranslationRenderActualTop = translationRenderActualTop,
            DrawingOffsetY = drawingOffsetY,
            RenderingWidth = renderingWidth,
            RenderingHeight = renderingHeight,
            ScalingCenterX = scalingCenterX,
            IdleColor = request.IdleColor,
            FocusingColor = request.FocusingColor,
            LyricGlyphClusters = lyricGlyphClusters,
            TransliterationGlyphClusters = transliterationGlyphClusters,
            TranslationGlyphClusters = translationGlyphClusters
        };
    }

    private static bool ShouldInferWords(FocusedTextEffectDefinition? definition)
    {
        if (definition is null) return false;
        foreach (var operation in definition.Operations)
        {
            if (operation.TypeId == HyPlayer.LyricEffects.Presets.FocusedTextBuiltInOperationTypes.HighlightReveal &&
                operation.Options.TryGetValue("untimedMode", out var highlightMode) &&
                highlightMode.Equals(nameof(UntimedHighlightMode.InferWords), StringComparison.OrdinalIgnoreCase))
                return true;
            if (operation.TypeId == HyPlayer.LyricEffects.Presets.FocusedTextBuiltInOperationTypes.GlyphLift &&
                operation.Options.TryGetValue("untimedMode", out var liftMode) &&
                liftMode.Equals(nameof(UntimedLiftMode.InferWords), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RetimeInferredTokens(
        IReadOnlyList<LyricTextToken> tokens,
        IReadOnlyList<LyricGlyphCluster> lyricClusters,
        long lineStartTime,
        long lineEndTime)
    {
        if (tokens.Count == 0 || tokens.Any(token => !token.IsInferred)) return;

        var glyphCounts = new int[tokens.Count];
        var sourceStart = 0;
        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var sourceEnd = sourceStart + tokens[tokenIndex].Text.Length;
            glyphCounts[tokenIndex] = lyricClusters.Count(cluster =>
                cluster.SourceStart >= 0 && cluster.SourceEnd > sourceStart && cluster.SourceStart < sourceEnd);
            sourceStart = sourceEnd;
        }

        var glyphWeights = glyphCounts.Select(count => Math.Max(1, count)).ToArray();
        var totalGlyphCount = Math.Max(1, glyphWeights.Sum());
        var lineDuration = Math.Max(0, lineEndTime - lineStartTime);
        var elapsedGlyphCount = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            var start = lineStartTime + lineDuration * elapsedGlyphCount / totalGlyphCount;
            elapsedGlyphCount += glyphWeights[index];
            var end = index == tokens.Count - 1
                ? lineEndTime
                : lineStartTime + lineDuration * Math.Min(elapsedGlyphCount, totalGlyphCount) / totalGlyphCount;
            tokens[index].StartTime = start;
            tokens[index].EndTime = end;
            tokens[index].Duration = end - start;
        }
    }

    private static void MapInferredTokenClusters(
        IReadOnlyList<LyricTextToken> tokens,
        IReadOnlyList<LyricGlyphCluster> clusters)
    {
        var sourceStart = 0;
        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var sourceEnd = sourceStart + tokens[tokenIndex].Text.Length;
            var tokenClusterCount = 0;
            for (var clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                var cluster = clusters[clusterIndex];
                if (cluster.SourceStart >= 0 && cluster.SourceEnd > sourceStart && cluster.SourceStart < sourceEnd)
                    tokenClusterCount++;
            }

            var tokenClusterIndex = 0;
            for (var clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                var cluster = clusters[clusterIndex];
                if (cluster.SourceStart < 0 || cluster.SourceEnd <= sourceStart || cluster.SourceStart >= sourceEnd)
                    continue;
                cluster.InferredTokenIndex = tokenIndex;
                cluster.InferredTokenClusterIndex = tokenClusterIndex++;
                cluster.InferredTokenClusterCount = Math.Max(1, tokenClusterCount);
            }
            sourceStart = sourceEnd;
        }
    }

    private static CanvasTextFormat CreateTextFormat(
        float fontSize,
        TextAlignment alignment,
        string? fontFamily,
        FontWeight fontWeight,
        CanvasWordWrapping wordWrapping)
    {
        return new CanvasTextFormat
        {
            FontSize = fontSize,
            HorizontalAlignment = alignment switch
            {
                TextAlignment.Right => CanvasHorizontalAlignment.Right,
                TextAlignment.Center => CanvasHorizontalAlignment.Center,
                _ => CanvasHorizontalAlignment.Left
            },
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = wordWrapping,
            Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
            FontFamily = fontFamily,
            FontWeight = fontWeight
        };
    }

    private static string WrapText(
        CanvasDrawingSession session,
        string text,
        CanvasTextFormat textFormat,
        float requestedWidth,
        float canvasHeight)
    {
        using var tmpTextLayout = new CanvasTextLayout(session, text, textFormat, int.MaxValue, canvasHeight);
        var span = text.AsSpan();
        var lastSpaceIndex = 0;
        var currentLineLength = 0.0;
        var sb = new StringBuilder();

        for (var i = 0; i <= span.Length; i++)
        {
            if (i != span.Length && span[i] is not (' ' or '　')) continue;

            var region = tmpTextLayout.GetCharacterRegions(lastSpaceIndex, i - lastSpaceIndex);
            var length = 0.0;
            if (region.Length > 0)
            {
                length += region[0].LayoutBounds.Width;
            }

            if (currentLineLength + length > requestedWidth)
            {
                if (lastSpaceIndex != 0)
                {
                    sb.Append('\n');
                    sb.Append(span[(lastSpaceIndex + 1)..i]);
                }
                else
                {
                    sb.Append(span[lastSpaceIndex..i]);
                }

                currentLineLength = 0;
                lastSpaceIndex = i;
                i++;
            }
            else
            {
                sb.Append(span[lastSpaceIndex..i]);
                currentLineLength += length;
                lastSpaceIndex = i;
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<LyricGlyphCluster> CollectGlyphClusters(
        CanvasDrawingSession layoutSession,
        LyricTextLayer layer,
        CanvasTextLayout layout,
        float drawOffsetX,
        float drawOffsetY,
        IReadOnlyList<int> sourceIndexMap,
        IReadOnlyList<int> tokenIndexMap,
        float dpi,
        int tokenCount,
        IReadOnlyList<int>? proportionalTokenWeights = null)
    {
        var collector = new LyricGlyphPlanCollector(layoutSession, layer, sourceIndexMap, tokenIndexMap, dpi);
        layout.DrawToTextRenderer(collector, drawOffsetX, drawOffsetY);
        var clusters = collector.Clusters.ToArray();
        if (proportionalTokenWeights is { Count: > 0 } && tokenCount > 0 && clusters.Length > 0)
        {
            // 整行音译没有逐 Word 映射时，按累计 GlyphUnit 比例映射正文时间轴。
            var totalWeight = Math.Max(1, proportionalTokenWeights.Sum(weight => Math.Max(1, weight)));
            for (var index = 0; index < clusters.Length; index++)
            {
                var position = (index + 0.5f) * totalWeight / clusters.Length;
                var cumulative = 0;
                var tokenIndex = 0;
                while (tokenIndex < tokenCount - 1)
                {
                    cumulative += Math.Max(1, proportionalTokenWeights[tokenIndex]);
                    if (position < cumulative) break;
                    tokenIndex++;
                }
                clusters[index].TokenStartIndex = tokenIndex;
                clusters[index].TokenEndIndexExclusive = tokenIndex + 1;
            }
        }
        LyricGlyphPlanCollector.FinalizeClusterIndexes(clusters, tokenCount);
        return clusters;
    }

    private static int[] CreateTokenGlyphWeights(
        IReadOnlyList<LyricGlyphCluster> clusters,
        int tokenCount)
    {
        var weights = new int[tokenCount];
        foreach (var cluster in clusters)
        {
            if ((uint)cluster.TokenStartIndex < (uint)weights.Length)
                weights[cluster.TokenStartIndex]++;
        }
        return weights;
    }

    private static float GetContentLeft(params CanvasTextLayout?[] layouts)
    {
        var left = float.MaxValue;
        for (var i = 0; i < layouts.Length; i++)
        {
            if (layouts[i] is null) continue;
            left = Math.Min(left, (float)layouts[i]!.LayoutBounds.Left);
        }

        return left == float.MaxValue ? 0 : left;
    }

    private static float GetContentRight(params CanvasTextLayout?[] layouts)
    {
        var right = 0f;
        for (var i = 0; i < layouts.Length; i++)
        {
            if (layouts[i] is null) continue;
            right = Math.Max(right, (float)layouts[i]!.LayoutBounds.Right);
        }

        return right;
    }

    private static float GetScalingCenterX(
        CanvasTextLayout textLayout,
        float renderStartX,
        float textPadding,
        TextAlignment alignment)
    {
        var bounds = textLayout.LayoutBounds;
        return alignment switch
        {
            TextAlignment.Center => (float)(bounds.Left + bounds.Width / 2) - renderStartX + textPadding,
            TextAlignment.Right => (float)bounds.Right - renderStartX + textPadding,
            _ => (float)bounds.Left - renderStartX + textPadding
        };
    }

    private static int[] CreateSourceIndexMap(string layoutText, string sourceText)
    {
        var map = new int[layoutText.Length];
        var sourceIndex = 0;
        for (var i = 0; i < layoutText.Length; i++)
        {
            if (layoutText[i] == '\n')
            {
                map[i] = -1;
                continue;
            }

            while (sourceIndex < sourceText.Length && sourceText[sourceIndex] != layoutText[i])
            {
                sourceIndex++;
            }

            map[i] = sourceIndex < sourceText.Length ? sourceIndex : -1;
            if (sourceIndex < sourceText.Length)
            {
                sourceIndex++;
            }
        }

        return map;
    }

    private static int[] CreateTokenIndexMap(
        IReadOnlyList<LyricTextToken> tokens,
        Func<LyricTextToken, string> textSelector)
    {
        if (tokens.Count == 0) return [];

        var totalLength = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            totalLength += textSelector(tokens[i]).Length;
        }

        var map = new int[totalLength];
        var position = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var text = textSelector(tokens[i]);
            for (var j = 0; j < text.Length && position < map.Length; j++)
            {
                map[position++] = i;
            }
        }

        return map;
    }

    private static ICanvasImage CreatePersistCache(
        CanvasDrawingSession session,
        float renderingWidth,
        float renderingHeight,
        float dpi,
        bool cacheRenderTarget,
        out CanvasDrawingSession drawingSession)
    {
        if (!cacheRenderTarget)
        {
            var commandList = new CanvasCommandList(session);
            drawingSession = commandList.CreateDrawingSession();
            return commandList;
        }

        var renderTarget = new CanvasRenderTarget(session, renderingWidth, renderingHeight, dpi);
        drawingSession = renderTarget.CreateDrawingSession();
        return renderTarget;
    }
}
