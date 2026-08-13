using HyPlayer.LyricRenderer.Animator;
using System;

namespace HyPlayer.LyricRenderer.RollingCalculators;

/// <summary>
/// Preserves the historical lyric-line rolling curve. Effect-chain Elastic uses a separate,
/// endpoint-strict curve and must not inherit this 0.7 amplitude.
/// </summary>
internal sealed class LineRollingElasticEase : EaseFunctionBase
{
    private const double Amplitude = 0.7;

    public float Springiness { get; init; } = 6;

    public float Oscillations { get; init; } = 1;

    protected override double EaseInCore(double normalizedTime)
    {
        var exponential = (Math.Exp(Springiness * normalizedTime) - 1) /
                          (Math.Exp(Springiness) - 1);
        return Amplitude * exponential *
               Math.Sin((Math.PI * 2 * Oscillations + Math.PI * 0.5) * normalizedTime);
    }
}
