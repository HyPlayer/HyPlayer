using System;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;

namespace HyPlayer.LyricRenderer.Effect;

public class EffectProperty
{
    public CanvasTransition Transition { get; set; } = new()
    {
        Duration = TimeSpan.FromSeconds(0.5),
        Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut }
    };

    public EffectProperty(EffectExpression expression, CanvasTransition? transition = null)
    {
        Expression = expression;
        if (transition is not null) Transition = transition;
    }

    public EffectExpression Expression { get; init; }

    public float GetValue(RenderingLyricLine lyricLine, RenderContext context)
    {
        return Transition.Animate(context.CurrentLyricTime, Expression(lyricLine, context));
    }
}
