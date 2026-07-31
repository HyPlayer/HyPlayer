using System;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;

namespace HyPlayer.LyricRenderer.Animator;

public class CanvasTransition
{
    private double _startTime = double.MinValue;
    private double _startValue;
    private double _targetValue = double.NaN;
    public TimeSpan Duration { get; set; }
    public EaseFunctionBase Easing { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };

    public virtual float Animate(long currentTime, double value)
    {
        if (double.IsNaN(_targetValue))
        {
            _startValue = value;
            _targetValue = value;
            _startTime = currentTime;
            return (float)value;
        }

        if (Math.Abs(value - _targetValue) > double.Epsilon)
        {
            var currentProgress = Math.Clamp((currentTime - _startTime) / Duration.TotalMilliseconds, 0, 1);
            _startValue = _startValue + (_targetValue - _startValue) * Easing.Ease(currentProgress);
            _targetValue = value;
            _startTime = currentTime;
        }

        var progress = Math.Clamp((currentTime - _startTime) / Duration.TotalMilliseconds, 0, 1);
        return (float)(_startValue + (_targetValue - _startValue) * Easing.Ease(progress));
    }
}