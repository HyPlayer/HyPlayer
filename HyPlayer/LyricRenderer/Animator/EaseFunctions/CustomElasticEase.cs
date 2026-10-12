using System;

namespace HyPlayer.LyricRenderer.Animator.EaseFunctions;

public class CustomElasticEase : EaseFunctionBase
{
    public float Springiness = 6;
    public float Oscillations = 1;

    protected override double EaseInCore(double normalizedTime)
        => HyPlayer.LyricRenderer.Text.FocusedTextProgress.GetElasticProgress(
            normalizedTime,
            Springiness,
            Oscillations);
}
