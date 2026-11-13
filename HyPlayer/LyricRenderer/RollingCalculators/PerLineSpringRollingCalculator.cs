using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using System;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public sealed class PerLineSpringRollingCalculator : LineRollingCalculator
{
    internal const double TightGapSeconds = 0.35;
    internal const double RelaxedGapSeconds = 1.2;
    internal const double TightResponseSeconds = 0.28;
    internal const double RelaxedResponseSeconds = 0.6;
    internal const double RelaxedDamping = 0.78;
    internal const double CascadeStepSeconds = 0.03;
    internal const double MinimumCascadeStepSeconds = 0.004;
    internal const double CascadeReserveSeconds = 0.05;
    internal const int CascadeTrailingLines = 2;
    internal const double TogetherResponseSeconds = 0.5;
    internal const double CorrectionResponseSeconds = 0.16;
    internal const double CorrectionDistance = 4;
    internal const double FollowResponseSeconds = 0.2;
    internal const double ReleaseResponseSeconds = 0.5;
    internal const double ReleaseDamping = 0.86;
    internal const int LargeJumpLines = 3;
    internal const double LargeJumpMilliseconds = 3000;

    public override float CalculateCurrentY(float fromY, float targetY, RenderingLyricLine currentLine,
        RenderContext context) => targetY;

    internal static void LineChangeSpring(double gapSeconds, out double responseSeconds, out double dampingRatio)
    {
        var relaxed = double.IsNaN(gapSeconds)
            ? 1
            : Math.Clamp((gapSeconds - TightGapSeconds) / (RelaxedGapSeconds - TightGapSeconds), 0, 1);
        responseSeconds = TightResponseSeconds + (RelaxedResponseSeconds - TightResponseSeconds) * relaxed;
        dampingRatio = 1 + (RelaxedDamping - 1) * relaxed;
    }

    internal static double CascadeStep(double gapSeconds, double responseSeconds, int visibleLines)
    {
        if (visibleLines <= 1) return 0;
        var spare = gapSeconds - SpringMotion.SettleSeconds(responseSeconds) - CascadeReserveSeconds;
        var step = Math.Min(CascadeStepSeconds, spare / (visibleLines - 1 + CascadeTrailingLines));
        return step < MinimumCascadeStepSeconds ? 0 : step;
    }
}
