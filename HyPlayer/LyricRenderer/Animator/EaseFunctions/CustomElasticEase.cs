using System;

namespace HyPlayer.LyricRenderer.Animator.EaseFunctions;

public class CustomElasticEase : EaseFunctionBase
{
    public float Springiness = 3;
    public float Oscillations = 0;
    
    protected override double EaseInCore(double normalizedTime)
    {
        double expo;
        expo = (Math.Exp(Springiness * normalizedTime) - 1.0) / (Math.Exp(Springiness) - 1.0);
        return expo * (Math.Sin((Math.PI * 2.0 * Oscillations + Math.PI * 0.5) * normalizedTime));
    }
}