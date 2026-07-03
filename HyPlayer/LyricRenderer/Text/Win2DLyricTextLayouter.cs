#nullable enable

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
        using var textFormat = CreateTextFormat(
            request.HiddenOnBlur ? request.LyricFontSize / 2 : request.LyricFontSize,
            request.Alignment,
            request.FontFamily,
            request.HiddenOnBlur ? FontWeights.Normal : FontWeights.SemiBold,
            CanvasWordWrapping.WholeWord);

        var hasTokenTransliteration = request.Tokens.Any(t => !string.IsNullOrEmpty(t.Transliteration));
        var transliterationActual = hasTokenTransliteration
            ? string.Join("", request.Tokens.Select(t => t.Transliteration ?? string.Empty))
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
                request.HiddenOnBlur ? request.TransliterationFontSize / 2 : request.TransliterationFontSize,
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
                request.HiddenOnBlur ? request.TranslationFontSize / 2 : request.TranslationFontSize,
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

        var actualText = request.Tokens.Count > 0
            ? string.Join("", request.Tokens.Select(t => t.Text))
            : request.Text;
        var wrappedText = WrapText(request.Session, actualText, textFormat, requestedWidth, request.CanvasHeight);
        var textLayout = new CanvasTextLayout(request.Session, wrappedText, textFormat, requestedWidth, request.CanvasHeight);
        var lyricSourceIndexMap = CreateSourceIndexMap(wrappedText, actualText);
        var lyricTokenIndexMap = CreateTokenIndexMap(request.Tokens, t => t.Text);
        var transliterationSourceIndexMap = transliterationActual is not null
            ? CreateSourceIndexMap(transliterationActual, transliterationActual)
            : [];
        var transliterationTokenIndexMap = hasTokenTransliteration
            ? CreateTokenIndexMap(request.Tokens, t => t.Transliteration ?? string.Empty)
            : [];

        var renderStartX = GetContentLeft(textLayout, transliterationLayout, translationLayout);
        var renderEndX = GetContentRight(textLayout, transliterationLayout, translationLayout);
        var drawOffsetX = -renderStartX + request.TextPadding;
        var scalingCenterX = GetScalingCenterX(textLayout, renderStartX, request.TextPadding, request.Alignment);
        var drawingOffsetY = (request.HiddenOnBlur ? request.LyricFontSize / 2 : request.LyricFontSize) / 8f;
        var renderingHeight = (float)textLayout.LayoutBounds.Height + drawingOffsetY + additionalHeight;
        var renderingWidth = Math.Max(1, renderEndX - renderStartX + request.TextPadding * 2);
        var useDynamicTransliteration = hasTokenTransliteration && transliterationLayout is not null;

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
        var sizePixelRect = new Rect(0, 0, renderingWidth, renderingHeight);
        float textRenderActualTop;
        float transliterationRenderTop = drawingOffsetY;
        IReadOnlyList<LyricGlyphCluster> lyricGlyphClusters;
        IReadOnlyList<LyricGlyphCluster> transliterationGlyphClusters = [];

        using (staticSession)
        using (defaultTextSession)
        using (transliterationSessionToDispose)
        {
            staticSession.Clear(Colors.Transparent);
            defaultTextSession.Clear(Colors.Transparent);
            transliterationSessionToDispose?.Clear(Colors.Transparent);
            var actualTop = drawingOffsetY;

            if (transliterationLayout is not null)
            {
                if (useDynamicTransliteration)
                {
                    transliterationSessionToDispose!.DrawTextLayout(transliterationLayout, drawOffsetX, actualTop, request.FocusingColor);
                }
                else
                {
                    staticSession.DrawTextLayout(transliterationLayout, drawOffsetX, actualTop, request.FocusingColor);
                }

                actualTop += (float)transliterationLayout.LayoutBounds.Height;
            }

            textRenderActualTop = actualTop;
            defaultTextSession.DrawTextLayout(textLayout, drawOffsetX, 0, request.FocusingColor);
            actualTop += (float)textLayout.LayoutBounds.Height;

            if (translationLayout is not null)
            {
                staticSession.DrawTextLayout(translationLayout, drawOffsetX, actualTop, request.FocusingColor);
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
                request.Tokens.Count);
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
                    request.Tokens.Count);
            }
        }

        return new LyricTextLayoutSnapshot
        {
            Text = actualText,
            Tokens = request.Tokens.ToArray(),
            TextLayout = textLayout,
            TranslationLayout = translationLayout,
            TransliterationLayout = transliterationLayout,
            StaticPersistCache = staticPersistCache,
            DefaultTextPersistCache = defaultTextPersistCache,
            DefaultTransliterationPersistCache = defaultTransliterationPersistCache,
            SizePixelRect = sizePixelRect,
            TextRenderActualTop = textRenderActualTop,
            DrawingOffsetY = drawingOffsetY,
            RenderingWidth = renderingWidth,
            RenderingHeight = renderingHeight,
            ScalingCenterX = scalingCenterX,
            FocusingColor = request.FocusingColor,
            LyricGlyphClusters = lyricGlyphClusters,
            TransliterationGlyphClusters = transliterationGlyphClusters
        };
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
        int tokenCount)
    {
        var collector = new LyricGlyphPlanCollector(layoutSession, layer, sourceIndexMap, tokenIndexMap, dpi);
        layout.DrawToTextRenderer(collector, drawOffsetX, drawOffsetY);
        var clusters = collector.Clusters.ToArray();
        LyricGlyphPlanCollector.FinalizeClusterIndexes(clusters, tokenCount);
        return clusters;
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
