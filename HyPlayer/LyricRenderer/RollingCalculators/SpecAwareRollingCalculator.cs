using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using System;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public sealed class SpecAwareRollingCalculator : LineRollingCalculator
{
    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine,
        RenderContext context)
    {
        var duration = Math.Max(1, context.Specs.LineChangeDurationMs);
        var elapsed = Math.Clamp(context.CurrentLyricTime - context.CurrentKeyframe, 0, duration);
        var progress = elapsed * 1f / duration;
        var eased = 1f - MathF.Pow(1f - progress, Math.Max(1f, context.Specs.LineChangeEasingPower));
        return fromY + (targetY - fromY) * eased;
    }
}
