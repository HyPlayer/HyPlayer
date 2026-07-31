using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Impressionist.Helpers;
using Impressionist.Quantizers;
using Impressionist.Selectors;

namespace HyPlayer.Platform.Imaging;

public static class ColorHelper
{
    private static readonly CelebiQuantizer _quantizer = new();

    public static async Task<Color> ExtractThemeColorFromStream(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var colors = await ImageDecoder.GetPixelColor(decoder);
        var color = Vector4.Zero;
        var count = colors.Count;
        foreach (var item in colors) color += item;
        color /= count;
        return Color.FromArgb((byte)color.X, (byte)color.Y, (byte)color.Z, (byte)color.W);
    }

    public static async Task<List<Vector3>> ExtractPaletteFromStream(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var colors = await ImageDecoder.GetPixelColor(decoder);
        var inputs = colors.Select(t => new ArgbColor(t)).ToList();
        var quantized = _quantizer.Quantize(inputs, 32).Colors;
        var selector = new HctColorSelector();
        var scored = selector.SelectColors(quantized)
            .Select(t => new Vector3(t.Red, t.Green, t.Blue))
            .ToList();
        var result = new List<Vector3>();
        var originalCount = scored.Count;
        for (var i = 0; i < 4; i++)
            // You know, it is always hard to fullfill a palette when you have no enough colors. So please forgive me when placing the same color over and over again.
            result.Add(scored[i % originalCount]);
        return result;
    }

    public static bool RGBVectorLStarIsDark(this Vector3 rgb)
    {
        var limitedColor = rgb / 255f;
        var y = 0.2126f * ChannelToLin(limitedColor.X) + 0.7152f * ChannelToLin(limitedColor.Y) +
                0.0722f * ChannelToLin(limitedColor.Z);
        var lStar = YToLStar(y);
        return lStar <= 50;
    }

    public static float ChannelToLin(float value)
    {
        if (value <= 0.04045f) return value / 12.92f;

        return (float)Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    public static float YToLStar(float y)
    {
        if (y <= 216f / 24389f)
            // The CIE standard states 0.008856 but 216/24389 is the intent for 0.008856451679036
            return y * (24389f /
                        27f); // The CIE standard states 903.3, but 24389/27 is the intent, making 903.296296296296296

        return (float)Math.Pow(y, 1f / 3f) * 116f - 16f;
    }
}