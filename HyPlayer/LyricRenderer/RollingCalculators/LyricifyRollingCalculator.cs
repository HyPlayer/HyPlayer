using System;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;

// Copyright WXRIW, in Lyricify

namespace HyPlayer.LyricRenderer.RollingCalculators;

internal class LyricifyRollingCalculator : LineRollingCalculator
{
    private const double duration = 620;

    private const double a = 0.882;
    private const double k = 0.836;
    private const double m = 3.08;
    private const double n = 3.14;

    protected static double f(double x)
    {
        if (x >= 0 && x <= a) return Math.Pow(x / a, m) * k / g(1);

        return g(x) / g(1);
    }

    protected static double g(double x)
    {
        return 1 - Math.Pow((1 - x) * 3 / 4 / (1 - a) + 1.0 / 4, n) * (1 - k);
    }


    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine,
        RenderContext context)
    {
        var progress = 1.0f;
        var gap = currentLine.Id - context.CurrentLyricLineIndex;
        if (!(fromY < targetY) && gap >= 0)
        {
            var theoryDuration = (float)duration /* * (Math.Log10(Math.Max(gap, 0.9)) + 1)*/;
            progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) / theoryDuration, 0, 1);
            progress = 1 - progress;
            progress = (float)f(progress);
            progress = 1 - progress;
        }
        else
        {
            progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) * 1.0f / 300, 0, 1);
        }

        return fromY + (targetY - fromY) * progress;
    }
}