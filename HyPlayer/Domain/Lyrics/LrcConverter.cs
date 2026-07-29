using ALRC.Abstraction;
using ALRC.Converters;
using CommunityToolkit.Mvvm.DependencyInjection;
using DynamicExpresso;
using HyPlayer.Domain.Lyrics.LyricEnhancers;
using HyPlayer.Domain.Settings;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Effect;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Text;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Color = System.Drawing.Color;

namespace HyPlayer.Domain.Lyrics;

public static class LrcConverter
{
    private static readonly ColorConverter ColorConverter = new();
    public static readonly List<ILyricEnhancer<bool>> LyricEnhancers = [
        new BreathLineEnhancer(),
        new NearbyLineAlignmentEnhancer(),
        new SublineAlignmentEnhancer(),
    ];

    public static List<RenderingLyricLine> Convert(
        ALRCFile alrc,
        List<LyricInfoMetadata> lyricMetadata = null,
        List<LyricInfoMetadata> songMetadata = null,
        bool optimizeLyric = false)
    {
        var result = new List<RenderingLyricLine>();
        if (optimizeLyric)
            foreach (var lyricEnhancer in LyricEnhancers)
            {
                alrc = lyricEnhancer.Enhance(true, alrc);
            }
        foreach (var alrcLine in alrc.Lines)
        {
            if (string.IsNullOrWhiteSpace(alrcLine.RawText) && alrcLine.Words is not { Count: > 0 } &&
                alrcLine.End - alrcLine.Start >= 1500)
            {
                // Empty Line
                result.Add(new ProgressBarRenderingLyricLine
                {
                    KeyFrames =
                    [
                        alrcLine.Start ?? 0,
                        alrcLine.End ?? 0
                    ],
                    StartTime = alrcLine.Start ?? 0,
                    EndTime = alrcLine.End ?? 0,
                });
                continue;
            }


            var line = new TextRenderingLyricLine
            {
                KeyFrames =
                [
                    alrcLine.Start ?? 0,
                    alrcLine.End ?? 0
                ],
                StartTime = alrcLine.Start ?? 0,
                EndTime = alrcLine.End ?? 0,
                Text = alrcLine.RawText,
                Transliteration = alrcLine.Transliteration,
                Translation = alrcLine.Translation
            };
            if (alrcLine.Words is { Count: > 0 })
            {
                line.Tokens = alrcLine.Words
                    .Select(w => new LyricTextToken(w.Word, w.Start, w.End, w.Transliteration)).ToList();
            }

            if (alrc.Header?.Styles?.FirstOrDefault(t => t.Id == alrcLine.LineStyle) is { } style)
            {
                line.Typography = new RenderTypography
                {
                    Alignment = style.Position switch
                    {
                        ALRCStylePosition.Left => TextAlignment.Left,
                        ALRCStylePosition.Center => TextAlignment.Center,
                        ALRCStylePosition.Right => TextAlignment.Right,
                        _ => null
                    },
                    FontWeight = style.Type == ALRCStyleAccent.Emphasise ? FontWeights.Bold : FontWeights.Normal,
                };
                line.HiddenOnBlur = style.HiddenOnBlur || style.Type == ALRCStyleAccent.Background;
                if (style.Color is not null)
                {
                    var colorRet = ColorConverter.ConvertFromString(style.Color);
                    if (colorRet is Color color)
                    {
                        line.Typography.FocusingColor = new Windows.UI.Color()
                        {
                            A = color.A,
                            R = color.R,
                            G = color.G,
                            B = color.B
                        };
                    }
                }
            }

            result.Add(line);
        }

        if (lyricMetadata is { Count: > 0 })
        {
            foreach (var lyricInfoMetadata in lyricMetadata)
            {
                result.Add(new ActionLyricLine()
                {
                    Text = $"{lyricInfoMetadata.DisplayName}: {lyricInfoMetadata.Value}",
                    ActionUri = lyricInfoMetadata.ActionUri
                });
            }
        }

        var settings = Ioc.Default.GetRequiredService<Setting>();
        var interpreter = new Interpreter();
        interpreter.SetVariable("this", new ExpressionService());
        const string opacityExpression = 
            """
            context.IsScrolling 
                ? Math.Max(
                    lyricLine.IsActive 
                        ? 1f
                        : Math.Clamp(GetGapValue(lyricLine, context, 0.4f, 0f), 0f, 1f)
                    , 0.4f)
               : (lyricLine.IsActive 
                    ? 1f 
                    : Math.Clamp(GetGapValue(lyricLine, context, 0.4f, 0f), 0f, 1f))
            """;
        var opacityFunc = interpreter.ParseAsDelegate<EffectExpression>(opacityExpression, "lyricLine", "context");
        const string blurExpression = "context.IsScrolling ? 0 : GetGapValue(lyricLine, context, 0f, 10f)";
        var blurFunc = interpreter.ParseAsDelegate<EffectExpression>(blurExpression, "lyricLine", "context");
        const string scaleExpression = "context.IsScrolling ? Math.Max(GetGapValue(lyricLine, context, 1f, 0.5f), 0.8f) : GetGapValue(lyricLine, context, 1f, 0.5f)";
        var scaleFunc = interpreter.ParseAsDelegate<EffectExpression>(scaleExpression, "lyricLine", "context");
        const string angleYExpression = "context.IsScrolling || lyricLine.IsActive ? 0f : Math.Clamp(-15f * Math.Abs(lyricLine.Id - context.CurrentLyricLineIndex), -60f, 60f)";
        var angleYFunc = interpreter.ParseAsDelegate<EffectExpression>(angleYExpression, "lyricLine", "context");

        foreach (var lyricLine in result)
        {

            if (settings.lyricRenderFade)
            {
                lyricLine.Effects.Add(
                    new LyricOpacityEffect
                    {
                        Opacity = new(opacityFunc)
                    });
            }

            if (settings.lyricRenderBlur)
            {
                lyricLine.Effects.Add(new LyricBlurEffect
                {
                    Amount = new(blurFunc)
                });
            }

            if (settings.lyricRenderScaleWhenFocusing)
            {
                lyricLine.FinalEffects.Add(new LyricTransform2DEffect
                {
                    XScale = new(scaleFunc),
                    YScale = new(scaleFunc)
                });
            }

            if (settings.lyricRenderTransform3D)
            {
                lyricLine.FinalEffects.Add(new LyricTransform3DEffect
                {
                    Duration = TimeSpan.FromSeconds(1),
                    AngleY = new(angleYFunc),
                });
            }
        }

        return result;
    }

    public static float GetGapValue(RenderingLyricLine lyricLine, RenderContext context, float start, float target)
    {
        var gap = Math.Abs((context.RenderOffsets[context.CurrentLyricLineIndex].Y - context.RenderOffsets[lyricLine.Id].Y) / context.ViewHeight);
        if (lyricLine.IsActive) gap = 0;
        var value = start + (target - start) * gap;
        return value;
    }

    public class ExpressionService
    {
        public float GetGapValue(RenderingLyricLine lyricLine, RenderContext context, float start, float target)
        {
            return LrcConverter.GetGapValue(lyricLine, context, start, target);
        }
    }
}
