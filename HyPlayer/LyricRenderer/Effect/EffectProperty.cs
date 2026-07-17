using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using System;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.Effect;

public class EffectProperty
{
    public CanvasTransition Transition = new()
    { 
        Duration = TimeSpan.FromSeconds(0.5),
        Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut }
    };

    public EffectExpression Expression { get; init; }

    public EffectProperty(EffectExpression expression, CanvasTransition? transition = null)
    {
        Expression = expression;
        if (transition is not null)
        {
            Transition = transition;
        }
    }

    public float GetValue(RenderingLyricLine lyricLine, RenderContext context)
    {
        return Transition.Animate(context.CurrentLyricTime, Expression(lyricLine, context));
    }
}