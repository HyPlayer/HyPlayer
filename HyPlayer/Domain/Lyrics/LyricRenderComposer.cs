#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml.Media.Animation;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using FontStyle = Windows.UI.Text.FontStyle;
using Size = Windows.Foundation.Size;

namespace HyPlayer.Domain.Lyrics;

public static class LyricRenderComposer
{
    public static void RenderOnDrawingSession(
        CanvasDrawingSession drawingSession, SongLyric lyric,
        TimeSpan position, LyricRenderOption renderOption, Size drawingSize, bool quickRender = false)
    {
        var currentTimeInLine = TimeSpan.Zero;
        if (!quickRender)
            currentTimeInLine = position - lyric.StartTime;
        using var textFormat = new CanvasTextFormat
        {
            FontSize = renderOption.FontSize,
            HorizontalAlignment = renderOption.HorizontalAlignment,
            VerticalAlignment = renderOption.VerticalAlignment,
            Options = CanvasDrawTextOptions.EnableColorFont,
            WordWrapping = CanvasWordWrapping.Wrap,
            Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
            FontStyle = renderOption.FontStyle,
            FontWeight = renderOption.FontWeight,
            FontFamily = renderOption.FontFamily
        };

        using var textFormatTranslation = new CanvasTextFormat
        {
            FontSize = 14,
            HorizontalAlignment = renderOption.HorizontalAlignment,
            Options = CanvasDrawTextOptions.EnableColorFont,
            WordWrapping = CanvasWordWrapping.Wrap,
            Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
            FontStyle = renderOption.FontStyle,
            FontFamily = renderOption.FontFamily
        };

        using var textLayout =
            new CanvasTextLayout(
                drawingSession, lyric.Text, textFormat,
                (float)drawingSize.Width, (float)drawingSize.Height);
        var textLayoutTranslation = lyric.HaveTranslation
            ? new CanvasTextLayout(drawingSession, lyric.Translation, textFormatTranslation, (float)drawingSize.Width,
                (float)drawingSize.Height)
            : null;
        var textLayoutRomaji = lyric.HaveRomaji
            ? new CanvasTextLayout(drawingSession, lyric.Romaji, textFormatTranslation, (float)drawingSize.Width,
                (float)drawingSize.Height)
            : null;

        drawingSession.DrawTextLayout(textLayout, 0, 0, renderOption.LyricIdleColor);
        if (textLayoutTranslation is not null)
            drawingSession.DrawTextLayout(textLayoutTranslation, 0, (float)textLayout.DrawBounds.Bottom + 4,
                renderOption.LyricIdleColor);
        if (textLayoutRomaji is not null)
            drawingSession.DrawTextLayout(textLayoutRomaji, 0,
                (float)textLayout.DrawBounds.Top - (float)textLayoutRomaji.DrawBounds.Height -
                8, renderOption.LyricIdleColor);

        if (!quickRender && lyric.Syllables is { Count: > 0 } syllables)
        {
            // 获取已高亮字符数
            var currentSyllable = GetCurrentSyllable(currentTimeInLine, syllables);
            var currentSyllableIndex = syllables.IndexOf(currentSyllable);
            var letterPosition = GetLetterPosition(currentSyllable, syllables);
            var highlightedGeometry =
                CreateHighlightedWordsGeometry(textLayout.GetCharacterRegions(0, letterPosition), drawingSession);
            var startTime =
                TimeSpan.FromMilliseconds(syllables.Take(currentSyllableIndex)
                    .Sum(syllable => syllable.Duration.TotalMilliseconds));
            var shouldEase = currentSyllableIndex == syllables.Count - 1 ||
                             currentSyllable.Duration.TotalSeconds > 1;
            var currentPercentage =
                GetCurrentWordPercentage(startTime.TotalMilliseconds, currentTimeInLine.TotalMilliseconds,
                    currentSyllable.Duration.TotalMilliseconds, shouldEase, renderOption);
            var currentWordGeometry = CreateCurrentWordGeometry(currentPercentage, drawingSession,
                textLayout.GetCharacterRegions(
                    letterPosition,
                    currentSyllable.Text.Length));
            var textGeometry = CanvasGeometry.CreateText(textLayout);
            var highlightTextGeometry =
                highlightedGeometry.CombineWith(textGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
            var currentTextGeometry =
                currentWordGeometry?.CombineWith(textGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);

            {
                var commandList = new CanvasCommandList(drawingSession);
                using (var ds = commandList.CreateDrawingSession())
                {
                    ds.FillGeometry(highlightTextGeometry, renderOption.HighlightColor);
                }

                var highlightedShadow = new ColorMatrixEffect
                {
                    Source = new GaussianBlurEffect
                    {
                        BlurAmount = renderOption.BlurAmount,
                        Source = commandList,
                        BorderMode = EffectBorderMode.Soft
                    },
                    ColorMatrix = GetColorMatrix(renderOption.ShadowColor)
                };
                drawingSession.DrawImage(highlightedShadow);
                drawingSession.FillGeometry(highlightTextGeometry, renderOption.HighlightColor);
            }

            if (currentTextGeometry is not null)
            {
                var commandList = new CanvasCommandList(drawingSession);
                using (var ds = commandList.CreateDrawingSession())
                {
                    ds.FillGeometry(currentTextGeometry, renderOption.HighlightColor);
                }

                var wordHighlightShadow = new ColorMatrixEffect
                {
                    Source = new GaussianBlurEffect
                    {
                        BlurAmount = renderOption.BlurAmount /* * (shouldEase ? shadowPercentage : 1)*/,
                        Source = commandList,
                        BorderMode = EffectBorderMode.Soft
                    },
                    ColorMatrix = GetColorMatrix(renderOption.ShadowColor)
                };
                drawingSession.DrawImage(wordHighlightShadow);
                drawingSession.FillGeometry(currentTextGeometry, renderOption.HighlightColor);


                drawingSession.FillGeometry(currentTextGeometry, renderOption.HighlightColor);
            }
        }
        else
        {
            var textGeometry = CanvasGeometry.CreateText(textLayout);
            drawingSession.FillGeometry(textGeometry, renderOption.HighlightColor);
        }
    }

    private static double GetCurrentWordPercentage(double startTime, double currentTime, double duration,
        bool shouldEase, LyricRenderOption renderOption)
    {
        return shouldEase
            ? renderOption.EaseFunction.Ease((currentTime - startTime) / duration)
            : (currentTime - startTime) / duration;
    }

    private static LyricSyllable GetCurrentSyllable(TimeSpan currentTime, List<LyricSyllable> syllables)
    {
        var time = TimeSpan.Zero;
        var currentSyllable = syllables[^1];
        foreach (var syllable in syllables)
        {
            if (syllable.Duration + time > currentTime)
            {
                currentSyllable = syllable;
                break;
            }

            time += syllable.Duration;
        }

        return currentSyllable;
    }

    private static int GetLetterPosition(LyricSyllable currentSyllable, List<LyricSyllable> syllables)
    {
        var index = syllables.IndexOf(currentSyllable);
        return syllables.Take(index).Sum(syllable => syllable.Text.Length);
    }

    private static CanvasGeometry? CreateCurrentWordGeometry(double currentPercentage,
        CanvasDrawingSession drawingSession,
        CanvasTextLayoutRegion[] currentRegions)
    {
        // 获取当前字符的 Bound
        // 获取正在播放单词的长度
        if (currentRegions is { Length: > 0 })
        {
            var lastRect = CanvasGeometry.CreateRectangle(
                drawingSession, (float)currentRegions[0].LayoutBounds.Left,
                (float)currentRegions[0].LayoutBounds.Top,
                (float)(currentRegions.Sum(t => t.LayoutBounds.Width) * currentPercentage),
                (float)currentRegions.Sum(t => t.LayoutBounds.Height));
            return lastRect;
        }

        return null;
    }

    private static CanvasGeometry CreateHighlightedWordsGeometry(CanvasTextLayoutRegion[] regions,
        ICanvasResourceCreator drawingSession)
    {
        var geos = new HashSet<CanvasGeometry>();
        foreach (var region in regions) geos.Add(CanvasGeometry.CreateRectangle(drawingSession, region.LayoutBounds));

        return CanvasGeometry.CreateGroup(drawingSession, geos.ToArray());
    }


    private static Matrix5x4 GetColorMatrix(Color color)
    {
        var matrix = new Matrix5x4();

        var r = ((float)color.R - 128) / 128;
        var g = ((float)color.G - 128) / 128;
        var b = ((float)color.B - 128) / 128;

        matrix.M11 = r;
        matrix.M12 = g;
        matrix.M13 = b;

        matrix.M21 = r;
        matrix.M22 = g;
        matrix.M23 = b;

        matrix.M31 = r;
        matrix.M32 = g;
        matrix.M33 = b;

        matrix.M44 = 1;

        return matrix;
    }
}
