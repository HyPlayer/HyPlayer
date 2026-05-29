using System;
using System.Numerics;
using Windows.UI;

namespace HyPlayer.Infrastructure.Imaging;

public static class ColorHelper
{
    public static Color GetReversedColor(Color color)
    {
        var grayLevel = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        if (grayLevel > 0.1)
            return Colors.Black;
        return Colors.White;
    }

    public static bool RGBVectorLStarIsDark(this Vector3 rgb)
    {
        var limitedColor = rgb / 255f;
        var y = 0.2126f * ChannelToLin(limitedColor.X) + 0.7152f * ChannelToLin(limitedColor.Y) + 0.0722f * ChannelToLin(limitedColor.Z);
        var lStar = YToLStar(y);
        return lStar <= 50;
    }

    public static float ChannelToLin(float value)
    {
        if (value <= 0.04045f)
        {
            return value / 12.92f;
        }
        else
        {
            return (float)Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    public static float YToLStar(float y)
    {
        if (y <= (216f / 24389f))
        {       // The CIE standard states 0.008856 but 216/24389 is the intent for 0.008856451679036
            return y * (24389f / 27f);  // The CIE standard states 903.3, but 24389/27 is the intent, making 903.296296296296296
        }
        else
        {
            return (float)Math.Pow(y, (1f / 3f)) * 116f - 16f;
        }
    }
}
