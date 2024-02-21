using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.Animator;

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
                // EaseOut is the same as EaseIn, except time is reversed & the result is flipped.
                return 1.0 - EaseInCore(1.0 - normalizedTime);
            case EasingMode.EaseInOut:
            default:
                // EaseInOut is a combination of EaseIn & EaseOut fit to the 0-1, 0-1 range.
                return (normalizedTime < 0.5)
                    ? EaseInCore(normalizedTime * 2.0) * 0.5
                    : (1.0 - EaseInCore((1.0 - normalizedTime) * 2.0)) * 0.5 + 0.5;
        }
    }
}

