using System;

namespace HyPlayer.LyricRenderer.Animator.EaseFunctions;

public class CustomCircleEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
        return 1.0 - Math.Sqrt(1.0 - normalizedTime * normalizedTime);
    }
}