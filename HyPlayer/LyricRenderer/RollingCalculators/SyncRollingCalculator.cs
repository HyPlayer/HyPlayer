using System;
using System.Diagnostics;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class SyncRollingCalculator : LineRollingCalculator
{
    public const double Duration = 500;
    
    public override double CalculateCurrentY(double fromY, double targetY, RenderingLyricLine currentLine, RenderContext context)
    {
        var gap = currentLine.Id - context.CurrentLyricLineIndex;
        if (gap < -3)
        {
            return targetY;
        }
        var targetOffset = (targetY - fromY);
        var v = targetOffset / Duration;
        var t = Math.Clamp(context.CurrentLyricTime - context.CurrentKeyframe, 0, Duration);
        var y = v * t;
        return fromY + (y);
    }
}