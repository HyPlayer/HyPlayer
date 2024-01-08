#nullable enable
using LyricParser.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml.Media.Animation;
using FontStyle = Windows.UI.Text.FontStyle;
using Size = Windows.Foundation.Size;

namespace HyPlayer.Classes;

public static class LyricRenderComposer
{
    public static void RenderOnDrawingSession(
        CanvasDrawingSession drawingSession, SongLyric lyric,
        TimeSpan position, LyricRenderOption renderOption, Size drawingSize, bool quickRender = false)
    {
        var _currentTimeInLine = TimeSpan.Zero;
        if (!quickRender)
            _currentTimeInLine = position - lyric.LyricLine.StartTime;
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
            FontFamily = renderOption.FontFamily,
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
                drawingSession, lyric.LyricLine.CurrentLyric, textFormat,
                (float)drawingSize.Width, (float)drawingSize.Height);
        var textLayoutTranslation = (lyric.HaveTranslation)
            ? new CanvasTextLayout(drawingSession, lyric.Translation, textFormatTranslation, (float)drawingSize.Width,
                                   (float)drawingSize.Height)
            : null;
        var textLayoutRomaji = (lyric.HaveRomaji)
            ? new CanvasTextLayout(drawingSession, lyric.Romaji, textFormatTranslation, (float)drawingSize.Width,
                                   (float)drawingSize.Height)
            : null;

        drawingSession.DrawTextLayout(textLayout, 0, 0, renderOption.LyricIdleColor);
        if (textLayoutTranslation is not null)
            drawingSession.DrawTextLayout(textLayoutTranslation, 0, (float)textLayout.DrawBounds.Bottom + 4,
                                          Colors.LightGray);
        if (textLayoutRomaji is not null)
            drawingSession.DrawTextLayout(textLayoutRomaji, 0,
                                          (float)textLayout.DrawBounds.Top - (float)textLayoutRomaji.DrawBounds.Height -
                                          8, Colors.LightGray);

        if (!quickRender && lyric.LyricLine is KaraokeLyricsLine karaokeLyricsLine)
        {
            // 获取已高亮字符数
            var currentWordInfo = GetCurrentWordInfo(_currentTimeInLine, karaokeLyricsLine);
            var currentWordIndex = karaokeLyricsLine.WordInfos.ToList().IndexOf(currentWordInfo);
            var letterPosition = GetLetterPosition(currentWordInfo, karaokeLyricsLine);
            var highlightedGeometry =
                CreateHighlightedWordsGeometry(textLayout.GetCharacterRegions(0, letterPosition), drawingSession);
            var startTime =
                TimeSpan.FromMilliseconds(karaokeLyricsLine.WordInfos.ToList().GetRange(0, currentWordIndex)
                                                           .Sum(p => p.Duration.TotalMilliseconds));
            var shouldEase = (currentWordIndex == karaokeLyricsLine.WordInfos.Count - 1 ||
                              currentWordInfo.Duration.TotalSeconds > 1);
            var currentPercentage =
                GetCurrentWordPercentage(startTime.TotalMilliseconds, _currentTimeInLine.TotalMilliseconds,
                                         currentWordInfo.Duration.TotalMilliseconds, shouldEase, renderOption);
            var currentWordGeometry = CreateCurrentWordGeometry(currentPercentage, drawingSession,
                                                                textLayout.GetCharacterRegions(
                                                                    letterPosition,
                                                                    currentWordInfo.CurrentWords.Length));
            var textGeometry = CanvasGeometry.CreateText(textLayout);
            var highlightTextGeometry =
                highlightedGeometry.CombineWith(textGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
            var currentTextGeometry =
                currentWordGeometry?.CombineWith(textGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);

            {
                var commandList = new CanvasCommandList(drawingSession);
                using (var ds = commandList.CreateDrawingSession())
                    ds.FillGeometry(highlightTextGeometry, renderOption.HighlightColor);
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
                    ds.FillGeometry(currentTextGeometry, renderOption.HighlightColor);
                var shadowPercentage = -0.5;
                if (shouldEase && false)
                {
                    shadowPercentage =
                           GetCurrentWordPercentage(startTime.TotalMilliseconds, _currentTimeInLine.TotalMilliseconds,
                                        currentWordInfo.Duration.TotalMilliseconds, false, renderOption);
                }
                var wordHighlightShadow = new ColorMatrixEffect
                {
                    Source = new GaussianBlurEffect
                    {
                        BlurAmount = (float)(renderOption.BlurAmount/* * (shouldEase ? shadowPercentage : 1)*/),
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
            : (currentTime - startTime) / (double)duration;
    }

    private static KaraokeWordInfo GetCurrentWordInfo(TimeSpan currentTime, KaraokeLyricsLine karaokeLyricsLine)
    {
        var wordInfos = (List<KaraokeWordInfo>)karaokeLyricsLine.WordInfos;
        var time = TimeSpan.Zero;
        var currentLyric = wordInfos.Last();
        //获取播放中单词在歌词的位置
        foreach (var item in wordInfos)
        {
            if (item.Duration + time > currentTime)
            {
                currentLyric = item;
                break;
            }

            time += item.Duration;
        }

        return currentLyric;
    }

    private static int GetLetterPosition(KaraokeWordInfo currentLyric, KaraokeLyricsLine karaokeLyricsLine)
    {
        var wordInfos = karaokeLyricsLine.WordInfos.ToList();
        var index = wordInfos.IndexOf(currentLyric);
        var letterPosition = wordInfos.GetRange(0, index).Sum(p => p.CurrentWords.Length);
        return letterPosition;
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
        foreach (var region in regions)
        {
            geos.Add(CanvasGeometry.CreateRectangle(drawingSession, region.LayoutBounds));
        }

        return CanvasGeometry.CreateGroup(drawingSession, geos.ToArray());
    }


    private static Matrix5x4 GetColorMatrix(Color color)
    {
        var matrix = new Matrix5x4();

        var R = ((float)color.R - 128) / 128;
        var G = ((float)color.G - 128) / 128;
        var B = ((float)color.B - 128) / 128;

        matrix.M11 = R;
        matrix.M12 = G;
        matrix.M13 = B;

        matrix.M21 = R;
        matrix.M22 = G;
        matrix.M23 = B;

        matrix.M31 = R;
        matrix.M32 = G;
        matrix.M33 = B;

        matrix.M44 = 1;

        return matrix;
    }
}

public abstract class EaseFunctionBase
{
    public EasingMode EasingMode { get; set; }

    protected abstract double EaseInCore(double normalizedTime);

    public double Ease(double normalizedTime)
    {
        switch (EasingMode)
        {
            case EasingMode.EaseIn:
                return EaseInCore(normalizedTime);
            case EasingMode.EaseOut:
                // EaseOut is the same as EaseIn, except time is reversed & the result is flipped.
                return 1.0 - EaseInCore(1.0 - normalizedTime);
            case EasingMode.EaseInOut:
            default:
                // EaseInOut is a combination of EaseIn & EaseOut fit to the 0-1, 0-1 range.
                return (normalizedTime < 0.5)
                    ? EaseInCore(normalizedTime * 2.0) * 0.5
                    : (1.0 - EaseInCore((1.0 - normalizedTime) * 2.0)) * 0.5 + 0.5;
        }
    }
}

public class CustomCircleEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
        return 1.0 - Math.Sqrt(1.0 - normalizedTime * normalizedTime);
    }
}

public class CustomExponentialEase : EaseFunctionBase
{
    public double Exponent { get; set; } = 2.0d;

    protected override double EaseInCore(double normalizedTime)
    {
        double factor = Exponent;
        if (Math.Abs(factor) < 0.00001)
        {
            return normalizedTime;
        }
        else
        {
            return (Math.Exp(factor * normalizedTime) - 1.0) / (Math.Exp(factor) - 1.0);
        }
    }
}

public class CustomSineEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
        return 1.0 - Math.Sin((1.0 - normalizedTime) * Math.PI * 0.5);
    }
}

public struct LyricRenderOption
{
    public float FontSize { get; set; }
    public CanvasHorizontalAlignment HorizontalAlignment { get; set; }
    public CanvasVerticalAlignment VerticalAlignment { get; set; }
    public FontStyle FontStyle { get; set; }
    public FontWeight FontWeight { get; set; }
    public string FontFamily { get; set; }
    public float BlurAmount { get; set; }
    public EaseFunctionBase EaseFunction { get; set; }

    public Color HighlightColor { get; set; }
    public Color LyricIdleColor { get; set; }
    public Color ShadowColor { get; set; }
}