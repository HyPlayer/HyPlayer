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

        var isRomajiTokenLine = request.Tokens.Any(t => t.Transliteration is not null);
        var transliterationActual = isRomajiTokenLine
            ? string.Join("", request.Tokens.Select(t => t.Transliteration))
            : request.Transliteration;

        CanvasTextLayout? transliterationLayout = null;
        CanvasTextLayout? translationLayout = null;
        var additionalHeight = 0.0f;
        var requestedWidth = Math.Clamp(request.Context.ItemWidth - request.TextPadding, 0, int.MaxValue);

        if ((!string.IsNullOrWhiteSpace(transliterationActual) || !string.IsNullOrWhiteSpace(request.Translation) || isRomajiTokenLine) &&
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

        var renderStartX = (float)textLayout.LayoutBounds.X;
        if (transliterationLayout is not null) renderStartX = Math.Min(renderStartX, (float)transliterationLayout.LayoutBounds.X);
        if (translationLayout is not null) renderStartX = Math.Min(renderStartX, (float)translationLayout.LayoutBounds.X);

        var tokenBounds = CreateTokenBounds(textLayout, request.Tokens, actualText, renderStartX, request.TextPadding, request.LiftAmount);
        var characterBounds = CreateCharacterBounds(textLayout, actualText, renderStartX, request.TextPadding, request.LiftAmount);
        var expandedBounds = request.Tokens.Count > 0
            ? tokenBounds.SelectMany(t => t).ToArray()
            : textLayout.GetCharacterRegions(0, actualText.Length)
                .Select(t => new Rect(t.LayoutBounds.X - renderStartX + request.TextPadding, t.LayoutBounds.Y, t.LayoutBounds.Width, t.LayoutBounds.Height))
                .ToArray();

        var scalingCenterX = (float)(request.Alignment switch
        {
            TextAlignment.Center => textLayout.LayoutBounds.Width / 2 + request.TextPadding,
            TextAlignment.Right => textLayout.LayoutBounds.Width + request.TextPadding,
            _ => request.TextPadding
        });
        var drawingOffsetY = (request.HiddenOnBlur ? request.LyricFontSize / 2 : request.LyricFontSize) / 8f;
        var renderingHeight = (float)textLayout.LayoutBounds.Height + drawingOffsetY + additionalHeight;
        var renderingWidth = (float)Math.Max(textLayout.LayoutBounds.Width,
            Math.Max(transliterationLayout?.LayoutBounds.Width ?? 0, translationLayout?.LayoutBounds.Width ?? 0)) + 32;

        var staticPersistCache = CreatePersistCache(request.Session, renderingWidth, renderingHeight, request.Context.Dpi, request.Context.Effects.CacheRenderTarget, out var staticSession);
        var defaultTextPersistCache = CreatePersistCache(request.Session, renderingWidth, renderingHeight, request.Context.Dpi, request.Context.Effects.CacheRenderTarget, out var defaultTextSession);
        var sizePixelRect = new Rect(0, 0, renderingWidth, renderingHeight);
        float textRenderActualTop;

        using (staticSession)
        using (defaultTextSession)
        {
            staticSession.Clear(Colors.Transparent);
            defaultTextSession.Clear(Colors.Transparent);
            var actualTop = drawingOffsetY;
            var drawOffsetX = -renderStartX + request.TextPadding;

            if (transliterationLayout is not null)
            {
                staticSession.DrawTextLayout(transliterationLayout, drawOffsetX, actualTop, request.FocusingColor);
                actualTop += (float)transliterationLayout.LayoutBounds.Height;
            }

            textRenderActualTop = actualTop;
            defaultTextSession.DrawTextLayout(textLayout, drawOffsetX, 0, request.FocusingColor);
            actualTop += (float)textLayout.LayoutBounds.Height;

            if (translationLayout is not null)
            {
                staticSession.DrawTextLayout(translationLayout, drawOffsetX, actualTop, request.FocusingColor);
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
            SizePixelRect = sizePixelRect,
            TokenBounds = tokenBounds,
            CharacterBounds = characterBounds,
            ExpandedBounds = expandedBounds,
            RenderStartX = renderStartX,
            TextRenderActualTop = textRenderActualTop,
            DrawingOffsetY = drawingOffsetY,
            RenderingWidth = renderingWidth,
            RenderingHeight = renderingHeight,
            ScalingCenterX = scalingCenterX
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

    private static IReadOnlyList<Rect[]> CreateTokenBounds(
        CanvasTextLayout textLayout,
        IReadOnlyList<LyricTextToken> tokens,
        string text,
        float renderStartX,
        float textPadding,
        float liftAmount)
    {
        if (tokens.Count == 0) return [];

        var tokenBounds = new List<Rect[]>();
        var alreadyLetterCount = 0;
        foreach (var token in tokens)
        {
            var region = textLayout.GetCharacterRegions(alreadyLetterCount, token.Text.Length);
            if (region is { Length: > 0 })
            {
                tokenBounds.Add([.. region.Select(t => new Rect(
                    t.LayoutBounds.X - renderStartX + textPadding,
                    t.LayoutBounds.Y + liftAmount,
                    t.LayoutBounds.Width,
                    t.LayoutBounds.Height))]);
                alreadyLetterCount += token.Text.Length;
            }
            else
            {
                tokenBounds.Add([]);
            }
        }

        return tokenBounds;
    }

    private static IReadOnlyList<Rect[]> CreateCharacterBounds(
        CanvasTextLayout textLayout,
        string text,
        float renderStartX,
        float textPadding,
        float liftAmount)
    {
        var characterBounds = new List<Rect[]>();
        for (var i = 0; i < text.Length; i++)
        {
            var region = textLayout.GetCharacterRegions(i, 1);
            characterBounds.Add([.. region.Select(t => new Rect(
                t.LayoutBounds.X - renderStartX + textPadding,
                t.LayoutBounds.Y + liftAmount,
                t.LayoutBounds.Width,
                t.LayoutBounds.Height))]);
        }

        return characterBounds;
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
