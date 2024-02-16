using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using ALRC.Abstraction;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using Color = System.Drawing.Color;

namespace HyPlayer.LyricRenderer.Converters;

public static class LrcConverter
{
    private static readonly ColorConverter ColorConverter = new();
    
    public static List<RenderingLyricLine> Convert(ALRCFile alrc)
    {
        var result = new List<RenderingLyricLine>();
        foreach (var alrcLine in alrc.Lines)
        {
            if (string.IsNullOrWhiteSpace(alrcLine.RawText) && alrcLine.Words is not { Count: > 0 })
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
                    HiddenOnBlur = true
                });
                continue;
            }
            
            
            var line = new SyllablesRenderingLyricLine
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
                Translation = alrcLine.LineTranslations?.FirstOrDefault().Value,
            };
            if (alrcLine.Words is { Count: > 0 })
            {
                line.IsSyllable = true;
                line.Syllables = alrcLine.Words.Select(w => new RenderingSyllable()
                {
                    StartTime = w.Start,
                    EndTime = w.End,
                    Syllable = w.Word,
                    Transliteration = w.Transliteration
                }).ToList();
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
        return result;
    }
}