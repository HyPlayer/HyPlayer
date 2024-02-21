using System;

namespace HyPlayer.LyricRenderer.Animator.EaseFunctions;

public class CustomExponentialEase : EaseFunctionBase
{
    public double Exponent { get; set; } = 2.0d;

    protected override double EaseInCore(double normalizedTime)
    {
        double factor = Exponent;
        if (Math.Abs(factor) < 0.00001)
        {
            return normalizedTime;
        }
        else
        {
            return (Math.Exp(factor * normalizedTime) - 1.0) / (Math.Exp(factor) - 1.0);
        }
    }
}