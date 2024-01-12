using System;
using HyPlayer.LyricRenderer.Abstraction.Render;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class SinRollingCalculator : LineRollingCalculator
{
    
    public const long AnimationDuration = 300;
    
    public override double CalculateCurrentY(double fromY, double targetY, int gap, long startTime, long currentTime)
    {
        var progress = Math.Clamp((currentTime - startTime) * 1.0 / AnimationDuration, 0 , 1);
        
        if (!(fromY < targetY) && gap >= 0)
        {
            // 往下走, 用正弦函数
            progress = Math.Clamp((currentTime - startTime) * 1.0 / (AnimationDuration * (Math.Log10(Math.Max(gap,0.9)) + 1)), 0, 1);
        }
        return fromY + (targetY - fromY) * progress;
    }
}