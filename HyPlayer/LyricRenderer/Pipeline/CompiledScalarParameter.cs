using System;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricRenderer.Animator;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed class CompiledScalarParameter
{
    private readonly LyricScalarExpression _expression;
    private readonly float? _maximum;
    private readonly float? _minimum;
    private readonly LyricTransitionDefinition? _transition;

    public CompiledScalarParameter(
        LyricScalarExpression expression,
        LyricTransitionDefinition? transition,
        float? minimum,
        float? maximum)
    {
        _expression = expression;
        _transition = transition;
        _minimum = minimum;
        _maximum = maximum;
    }

    public ScalarParameterRuntime CreateRuntime()
    {
        return new ScalarParameterRuntime(_expression, _transition, _minimum, _maximum);
    }
}

internal sealed class ScalarParameterRuntime
{
    private readonly LyricScalarExpression _expression;
    private readonly float? _maximum;
    private readonly float? _minimum;
    private readonly CanvasTransition? _transition;

    public ScalarParameterRuntime(
        LyricScalarExpression expression,
        LyricTransitionDefinition? transition,
        float? minimum,
        float? maximum)
    {
        _expression = expression;
        _minimum = minimum;
        _maximum = maximum;
        _transition = transition is null || transition.DurationMs <= 0
            ? null
            : new CanvasTransition
            {
                Duration = TimeSpan.FromMilliseconds(transition.DurationMs),
                Easing = LyricEasingFactory.Create(transition)
            };
    }

    public float Evaluate(LyricRenderOperationContext context)
    {
        var value = _expression(context.Line, context.Frame, context.Functions);
        if (!float.IsFinite(value)) throw new InvalidOperationException("表达式返回了 NaN 或 Infinity。");
        if (_minimum is { } minimum) value = Math.Max(value, minimum);
        if (_maximum is { } maximum) value = Math.Min(value, maximum);
        return _transition?.Animate(context.Frame.CurrentTimeMs, value) ?? value;
    }
}
