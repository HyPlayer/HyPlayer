using System;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Abstraction;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class ElasticEaseRollingCalculator : LineRollingCalculator
{
    public ElasticEaseRollingCalculator()
    {
    }

    public const long AnimationDuration = 630;
    private float springiness = 3;
    private float oscillations = 0;

    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine,
        RenderContext context)
    {
        float progress = 1;
        var gap = currentLine.Id - context.CurrentLyricLineIndex;
        if (gap > -3)
        {
            if (!(fromY < targetY) && gap >= 0)
            {
                var theoryTime = AnimationDuration * ((float)Math.Log10(Math.Max(gap, 0.9)) + 1);
                progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) / theoryTime, 0, 1);
                progress = 1 - progress;
                progress = (float)EaseInCore(progress);
                progress = 1 - progress;
                /*

                var expo = (Math.Exp(springiness * progress) - 1.0) / (Math.Exp(springiness) - 1.0);
                progress = expo * (Math.Sin((Math.PI * 2.0 * oscillations + Math.PI * 0.5) * progress));
                progress = 1 - progress;*/
            }
            else
            {
                progress = Math.Clamp(
                    (context.CurrentLyricTime - context.CurrentKeyframe) * 1.0f / AnimationDuration *
                    ((float)Math.Log10(-gap + 15) + 1), 0, 1);
            }
        }

        return fromY + (targetY - fromY) * progress;
    }

    protected double EaseInCore(float normalizedTime)
    {
        double expo;

        expo = (Math.Exp(springiness * normalizedTime) - 1.0) / (Math.Exp(springiness) - 1.0);


        return expo * (Math.Sin((Math.PI * 2.0 * oscillations + Math.PI * 0.5) * normalizedTime));
    }
}