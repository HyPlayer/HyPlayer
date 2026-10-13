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
    protected virtual float OnsetProgress => 0;

    private const float MaxCarriedVelocity = 2;
    private const double MaxLeadMilliseconds = 100;
    private const double SlopeStep = 1e-4;
    private double _initialSlope = double.NaN;

    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine,
        RenderContext context)
    {
        if (context.CurrentLyricTime < context.CurrentKeyframe) return targetY;
        var gap = currentLine.RuntimeIndex - context.CurrentLyricLineIndex;
        var theoryTime = AnimationDuration * ((float)Math.Log10(Math.Max(gap, MaxEasingPercent)) + 1);
        var progress = Math.Clamp((float)(context.PreciseLyricTime - context.CurrentKeyframe) / theoryTime, 0, 1);
        var eased = (float)EaseFunction.Ease(progress);
        var onset = OnsetProgress;
        if (onset <= 0) return fromY + (targetY - fromY) * eased;

        var snapshots = context.SnapshotRenderOffsets;
        var velocity = (uint)currentLine.RuntimeIndex < (uint)snapshots.Count
            ? Math.Clamp(snapshots[currentLine.RuntimeIndex].VelocityY, -MaxCarriedVelocity, MaxCarriedVelocity)
            : 0;
        var start = fromY + velocity *
            (float)Math.Clamp(context.CurrentKeyframe - context.SnapshotLyricTime, 0, MaxLeadMilliseconds);
        var y = start + (targetY - start) * eased;
        if (progress >= onset) return y;

        if (double.IsNaN(_initialSlope)) _initialSlope = EaseFunction.Ease(SlopeStep) / SlopeStep;
        var remaining = 1 - progress / onset;
        return (float)(y + (velocity * theoryTime - (targetY - start) * _initialSlope) *
            progress * remaining * remaining);
    }
}

