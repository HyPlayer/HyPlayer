using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.RollingCalculators;
using TUnit.Core;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Playback.Tests;

public sealed class LineRollingElasticEaseTests
{
    [Test]
    [Arguments(EasingMode.EaseIn)]
    [Arguments(EasingMode.EaseOut)]
    [Arguments(EasingMode.EaseInOut)]
    public void LineRolling_ShouldStartAndEndAtExactEndpoints(EasingMode mode)
    {
        var rolling = new LineRollingElasticEase { EasingMode = mode };
        var effect = new CustomElasticEase { EasingMode = mode };

        AssertClose(0, rolling.Ease(0));
        AssertClose(1, rolling.Ease(1));
        AssertClose(0, effect.Ease(0));
        AssertClose(1, effect.Ease(1));
    }

    private static void AssertClose(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}
