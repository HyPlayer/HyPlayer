using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using System;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class SyncRollingCalculator : LineRollingCalculator
{
    public const float Duration = 200;

    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine, RenderContext context)
    {
        var targetOffset = (targetY - fromY);
        var v = targetOffset / Duration;
        var t = Math.Clamp(context.CurrentLyricTime - context.CurrentKeyframe, 0, Duration);
        var y = v * t;
        return fromY + (y);
    }
}