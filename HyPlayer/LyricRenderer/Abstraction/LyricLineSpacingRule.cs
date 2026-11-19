using System;

namespace HyPlayer.LyricRenderer.Abstraction;

public static class LyricLineSpacingRule
{
    public const int MinimumAdjustment = -30;
    public const int MaximumAdjustment = 200;
    private const float BaselinePerFontSize = 0.22f;
    private const float TightestPerFontSize = 0.25f;
    private const int TightestPixels = 12;

    public static int Resolve(int adjustment, float lyricFontSize)
    {
        var size = float.IsFinite(lyricFontSize) ? Math.Max(0, lyricFontSize) : 0;
        var baseline = (int)MathF.Round(size * BaselinePerFontSize);
        var tightest = -(int)Math.Min(TightestPixels, MathF.Floor(size * TightestPerFontSize));
        return Math.Max(tightest, baseline + Math.Clamp(adjustment, MinimumAdjustment, MaximumAdjustment));
    }
}
