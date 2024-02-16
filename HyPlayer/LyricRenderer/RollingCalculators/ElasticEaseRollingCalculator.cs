using System;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class ElasticEaseRollingCalculator : LineRollingCalculator
{

    private readonly CustomElasticEase _ease = new();
    
    public const long AnimationDuration = 630;


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
                progress = (float)_ease.Ease(progress);
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
}