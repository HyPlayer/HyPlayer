using ALRC.Abstraction;
using ALRC.Converters;
using CommunityToolkit.Mvvm.DependencyInjection;
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

        foreach (var lyricLine in result)
        {
            if (settings.lyricRenderFade)
            {
                lyricLine.Effects.Add(
                    new LyricOpacityEffect
                    {
                        Opacity = new((lyricLine, context) =>
                        {
                            var opacity = Math.Clamp(GetGapValue(lyricLine, context, 0.4f, 0f), 0, 1);
                            if (lyricLine.IsActive) opacity = 1;
                            if (context.IsScrolling) opacity = MathF.Max(opacity, 0.4f);
                            return (float)opacity;
                        })
                    });
            }

            if (settings.lyricRenderBlur)
            {

                lyricLine.Effects.Add(new LyricBlurEffect
                {
                    Amount = new((lyricLine, context) =>
                    {
                        var blur = GetGapValue(lyricLine, context, 0f, 10f);
                        blur = (context.IsScrolling) ? 0 : blur;
                        return blur;
                    })
                });
            }


            if (settings.lyricRenderScaleWhenFocusing)
            {
                lyricLine.Effects.Add(new LyricTransform2DEffect
                {
                    XScale = new((lyricLine, context) =>
                    {
                        var scale = GetGapValue(lyricLine, context, 1f, 0.5f);
                        if (context.IsScrolling) scale = Math.Max(scale, 0.8f);
                        return scale;
                    }),
                    YScale = new((lyricLine, context) =>
                    {
                        var scale = GetGapValue(lyricLine, context, 1f, 0.5f);
                        if (context.IsScrolling) scale = Math.Max(scale, 0.8f);
                        return scale;
                    })
                });
            }

            if (settings.lyricRenderTransform3D)
            {
                lyricLine.Effects.Add(new LyricTransform3DEffect
                {
                    AngleY = new((lyricLine, context) =>
                    {
                        var gap = Math.Abs(lyricLine.Id - context.CurrentLyricLineIndex);
                        var angle = Math.Clamp(-15 * gap, -60, 60);
                        if (context.IsScrolling || lyricLine.IsActive) angle = 0;
                        return angle;
                    }, new CanvasTransition { Duration = TimeSpan.FromSeconds(1) }),
                });
            }
        }

        return result;
    }


    private static float GetGapValue(RenderingLyricLine lyricLine, RenderContext context, float start, float target)
    {
        var gap = Math.Abs((context.RenderOffsets[context.CurrentLyricLineIndex].Y - context.RenderOffsets[lyricLine.Id].Y) / context.ViewHeight);
        if (lyricLine.IsActive) gap = 0;
        var value = start + (target - start) * gap;
        return value;
    }
}
