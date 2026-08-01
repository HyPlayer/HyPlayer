using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Windows.UI.Text;
using Windows.UI.Xaml;
using ALRC.Abstraction;
using ALRC.Converters;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Lyrics.LyricEnhancers;
using HyPlayer.Domain.Settings;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Effect;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Text;
using Color = System.Drawing.Color;

namespace HyPlayer.Domain.Lyrics;

public static class LrcConverter
{
    private static readonly ColorConverter _colorConverter = new();

    public static List<ILyricEnhancer<bool>> LyricEnhancers { get; } =
    [
        new BreathLineEnhancer(),
        new NearbyLineAlignmentEnhancer(),
        new SublineAlignmentEnhancer()
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
                alrc = lyricEnhancer.Enhance(true, alrc);
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
                    EndTime = alrcLine.End ?? 0
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
                line.Tokens = alrcLine.Words
                    .Select(w => new LyricTextToken(w.Word, w.Start, w.End, w.Transliteration)).ToList();

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
                    FontWeight = style.Type == ALRCStyleAccent.Emphasise ? FontWeights.Bold : FontWeights.Normal
                };
                line.HiddenOnBlur = style.HiddenOnBlur || style.Type == ALRCStyleAccent.Background;
                if (style.Color is not null)
                {
                    var colorRet = _colorConverter.ConvertFromString(style.Color);
                    if (colorRet is Color color)
                        line.Typography.FocusingColor = new Windows.UI.Color
                        {
                            A = color.A,
                            R = color.R,
                            G = color.G,
                            B = color.B
                        };
                }
            }

            result.Add(line);
        }

        if (lyricMetadata is { Count: > 0 })
            foreach (var lyricInfoMetadata in lyricMetadata)
                result.Add(new ActionLyricLine
                {
                    Text = $"{lyricInfoMetadata.DisplayName}: {lyricInfoMetadata.Value}",
                    ActionUri = lyricInfoMetadata.ActionUri
                });

        var settings = Ioc.Default.GetRequiredService<LyricSettings>();

        foreach (var lyricLine in result)
        {
            if (settings.LyricRenderFade)
            {
                lyricLine.Effects.Add(new LyricOpacityEffect
                {
                    Opacity = new EffectProperty((line, context) =>
                    {
                        var opacity = Math.Clamp(GetGapValue(line, context, 0.4f, 0f), 0, 1);
                        if (line.IsActive) opacity = 1;
                        if (context.IsScrolling) opacity = MathF.Max(opacity, 0.4f);
                        return opacity;
                    })
                });
            }

            if (settings.LyricRenderBlur)
            {
                lyricLine.Effects.Add(new LyricBlurEffect
                {
                    Amount = new EffectProperty((line, context) =>
                    {
                        var blur = GetGapValue(line, context, 0f, 10f);
                        return context.IsScrolling ? 0 : blur;
                    })
                });
            }

            if (settings.LyricRenderScaleWhenFocusing)
            {
                lyricLine.FinalEffects.Add(new LyricTransform2DEffect
                {
                    XScale = new EffectProperty((line, context) =>
                    {
                        var scale = GetGapValue(line, context, 1f, 0.5f);
                        return context.IsScrolling ? Math.Max(scale, 0.8f) : scale;
                    }),
                    YScale = new EffectProperty((line, context) =>
                    {
                        var scale = GetGapValue(line, context, 1f, 0.5f);
                        return context.IsScrolling ? Math.Max(scale, 0.8f) : scale;
                    })
                });
            }

            if (settings.LyricRenderTransform3D)
            {
                lyricLine.FinalEffects.Add(new LyricTransform3DEffect
                {
                    Duration = TimeSpan.FromSeconds(1),
                    AngleY = new EffectProperty((line, context) =>
                    {
                        var gap = Math.Abs(line.Id - context.CurrentLyricLineIndex);
                        var angle = Math.Clamp(-15 * gap, -60, 60);
                        return context.IsScrolling || line.IsActive ? 0 : angle;
                    })
                });
            }
        }

        return result;
    }

    private static float GetGapValue(RenderingLyricLine lyricLine, RenderContext context, float start, float target)
    {
        var gap = Math.Abs((context.RenderOffsets[context.CurrentLyricLineIndex].Y -
                            context.RenderOffsets[lyricLine.Id].Y) / context.ViewHeight);
        if (lyricLine.IsActive) gap = 0;
        return start + (target - start) * gap;
    }
}
