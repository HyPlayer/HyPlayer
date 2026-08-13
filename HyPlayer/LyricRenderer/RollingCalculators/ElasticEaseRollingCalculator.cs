using HyPlayer.LyricRenderer.Animator;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class ElasticEaseRollingCalculator : EaseRollingCalculator
{
    protected override EaseFunctionBase EaseFunction { get; set; } =
        new LineRollingElasticEase { EasingMode = EasingMode.EaseOut };
    protected override long AnimationDuration { get; set; } = 1300;
    protected override double MaxEasingPercent { get; set; } = 0.9;
}
