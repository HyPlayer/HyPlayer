using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using System;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public abstract class EaseRollingCalculator : LineRollingCalculator
{
    protected abstract EaseFunctionBase EaseFunction { get; set; }

    protected abstract long AnimationDuration { get; set; }
    protected virtual double MaxEasingPercent { get; set; } = 1;

    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine,
        RenderContext context)
    {
        float progress = 1;
        var gap = currentLine.RuntimeIndex - context.CurrentLyricLineIndex;
        if (!(fromY < targetY) && gap >= 0)
        {
            var theoryTime = AnimationDuration * ((float)Math.Log10(Math.Max(gap, MaxEasingPercent)) + 1);
            progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) / theoryTime, 0, 1);
            progress = (float)EaseFunction.Ease(progress);
        }
        else
        {
            var theoryTime = AnimationDuration * ((float)Math.Log10(Math.Max(gap, MaxEasingPercent)) + 1);
            progress = Math.Clamp((context.CurrentLyricTime - context.CurrentKeyframe) / theoryTime, 0, 1);
            progress = (float)EaseFunction.Ease(progress);
        }
        if (progress != 0)
        {
            return fromY + (targetY - fromY) * progress;
        }
        else
        {
            return targetY; //弄个Fallback，如果progress是0那就直接返回它应到的目标位置
        }
    }
}

