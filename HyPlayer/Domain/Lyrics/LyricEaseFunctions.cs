using System;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Domain.Lyrics;

public abstract class EaseFunctionBase
{
    public EasingMode EasingMode { get; set; }

    protected abstract double EaseInCore(double normalizedTime);

    public double Ease(double normalizedTime)
    {
        switch (EasingMode)
        {
            case EasingMode.EaseIn:
                return EaseInCore(normalizedTime);
            case EasingMode.EaseOut:
                return 1.0 - EaseInCore(1.0 - normalizedTime);
            case EasingMode.EaseInOut:
            default:
                return normalizedTime < 0.5
                    ? EaseInCore(normalizedTime * 2.0) * 0.5
                    : (1.0 - EaseInCore((1.0 - normalizedTime) * 2.0)) * 0.5 + 0.5;
        }
    }
}

public class CustomCircleEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
        return 1.0 - Math.Sqrt(1.0 - normalizedTime * normalizedTime);
    }
}

public class CustomExponentialEase : EaseFunctionBase
{
    public double Exponent { get; set; } = 2.0d;

    protected override double EaseInCore(double normalizedTime)
    {
        var factor = Exponent;
        if (Math.Abs(factor) < 0.00001) return normalizedTime;

        return (Math.Exp(factor * normalizedTime) - 1.0) / (Math.Exp(factor) - 1.0);
    }
}

public class CustomSineEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
        return 1.0 - Math.Sin((1.0 - normalizedTime) * Math.PI * 0.5);
    }
}
