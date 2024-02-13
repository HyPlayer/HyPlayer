using System;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class SinRollingCalculator : LineRollingCalculator
{
    
    public const long AnimationDuration = 300;
    
    public override double CalculateCurrentY(double fromY, double targetY, RenderingLyricLine currentLine, RenderContext context)
    {
        var gap = currentLine.Id - context.CurrentLyricLineIndex;
        var progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) * 1.0 / AnimationDuration, 0 , 1);
        
        if (!(fromY < targetY) && gap >= 0)
        {
            // 往下走, 用正弦函数
            progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) * 1.0 / (AnimationDuration * (Math.Log10(Math.Max(gap,0.9)) + 1)), 0, 1);
        }
        return fromY + (targetY - fromY) * progress;
    }
}