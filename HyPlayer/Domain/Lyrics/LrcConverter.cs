using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Windows.UI.Text;
using Windows.UI.Xaml;
using ALRC.Abstraction;
using ALRC.Converters;
using HyPlayer.Domain.Lyrics.LyricEnhancers;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Text;
using Color = System.Drawing.Color;

namespace HyPlayer.Domain.Lyrics;

public static class LrcConverter
{
    private static readonly ColorConverter ColorConverter = new();

    public static readonly List<ILyricEnhancer<bool>> LyricEnhancers =
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
                    var colorRet = ColorConverter.ConvertFromString(style.Color);
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

        return result;
    }
}