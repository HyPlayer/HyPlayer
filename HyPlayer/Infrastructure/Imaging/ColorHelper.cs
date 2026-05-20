using System;
using Windows.UI;

namespace HyPlayer.Infrastructure.Imaging;

internal class ColorHelper
{
    public static Color GetReversedColor(Color color)
    {
        var grayLevel = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        if (grayLevel > 0.1)
            return Colors.Black;
        return Colors.White;
    }

    public static Color FromHsv(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r, g, b;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(
            255,
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255)
        );
    }
}
