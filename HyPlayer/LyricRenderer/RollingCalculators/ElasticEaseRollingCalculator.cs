using System;
using System.Diagnostics;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class ElasticEaseRollingCalculator : LineRollingCalculator
{
    public ElasticEaseRollingCalculator()
    {
    }
    public const long AnimationDuration = 630;
    private double springiness = 3;
    private double oscillations = 0;

    public override double CalculateCurrentY(double fromY, double targetY, int gap, long startTime, long currentTime)
    {
        double progress = 1;
        if (gap > -3)
            if (!(fromY < targetY) && gap >= 0)
            {
                var theoryTime = AnimationDuration * (Math.Log10(Math.Max(gap, 0.9)) + 1);
                progress = Math.Clamp((currentTime - startTime) / theoryTime, 0, 1);
                progress = 1 - progress;
                progress = EaseInCore(progress);
                progress = 1 - progress;
                /*

                var expo = (Math.Exp(springiness * progress) - 1.0) / (Math.Exp(springiness) - 1.0);
                progress = expo * (Math.Sin((Math.PI * 2.0 * oscillations + Math.PI * 0.5) * progress));
                progress = 1 - progress;*/

            }
            else
            {
                progress = Math.Clamp((currentTime - startTime) * 1.0 / AnimationDuration * (Math.Log10(-gap + 15) + 1), 0, 1);
            }
        return fromY + (targetY - fromY) * progress;

    }

    protected double EaseInCore(double normalizedTime)
    {
        double expo;

        expo = (Math.Exp(springiness * normalizedTime) - 1.0) / (Math.Exp(springiness) - 1.0);


        return expo * (Math.Sin((Math.PI * 2.0 * oscillations + Math.PI * 0.5) * normalizedTime));
    }
}