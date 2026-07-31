using Windows.UI.Xaml.Media.Animation;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;

namespace HyPlayer.LyricRenderer.Pipeline;

internal static class LyricEasingFactory
{
    public static EaseFunctionBase Create(LyricTransitionDefinition transition)
    {
        EaseFunctionBase easing = transition.EasingId.ToLowerInvariant() switch
        {
            "linear" => new LinearEase(),
            "sine" => new CustomSineEase(),
            "exponential" => new CustomExponentialEase
            {
                Exponent = GetArgument(transition, "exponent", 2)
            },
            "elastic" => new CustomElasticEase
            {
                Springiness = (float)GetArgument(transition, "springiness", 6),
                Oscillations = (float)GetArgument(transition, "oscillations", 1)
            },
            "bounce" => new CustomBounceEase
            {
                Bounces = (int)GetArgument(transition, "bounces", 3),
                Bounciness = GetArgument(transition, "bounciness", 2)
            },
            _ => new CustomCircleEase()
        };
        easing.EasingMode = transition.Mode.ToLowerInvariant() switch
        {
            "in" => EasingMode.EaseIn,
            "inout" => EasingMode.EaseInOut,
            _ => EasingMode.EaseOut
        };
        return easing;
    }

    private static double GetArgument(LyricTransitionDefinition transition, string key, double fallback)
    {
        return transition.Arguments.TryGetValue(key, out var value) ? value : fallback;
    }
}

internal sealed class LinearEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        return normalizedTime;
    }
}
