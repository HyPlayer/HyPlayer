using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class CircleEaseRollingCalculator : EaseRollingCalculator
{
    protected override EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase() { EasingMode = EasingMode.EaseOut };
    protected override long AnimationDuration { get; set; } = 700;
}