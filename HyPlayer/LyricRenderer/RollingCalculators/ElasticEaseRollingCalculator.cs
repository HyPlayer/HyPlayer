using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using System;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.LyricRenderer.Animator;

namespace HyPlayer.LyricRenderer.RollingCalculators;

public class ElasticEaseRollingCalculator : EaseRollingCalculator
{
    protected override EaseFunctionBase EaseFunction { get; set; } = new CustomElasticEase() { EasingMode = EasingMode.EaseOut };
    protected override long AnimationDuration { get; set; } = 1300;

    protected override double MaxEasingPercent { get; set; } = 1;
}
