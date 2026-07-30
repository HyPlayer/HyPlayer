using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed class CompiledScalarParameter
{
    private readonly LyricScalarExpression _expression;
    private readonly LyricTransitionDefinition? _transition;
    private readonly float? _minimum;
    private readonly float? _maximum;

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

    public ScalarParameterRuntime CreateRuntime() => new(_expression, _transition, _minimum, _maximum);
}

internal sealed class ScalarParameterRuntime
{
    private readonly LyricScalarExpression _expression;
    private readonly CanvasTransition? _transition;
    private readonly float? _minimum;
    private readonly float? _maximum;

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

internal sealed class CompiledColorParameter
{
    private readonly LyricColorExpression _expression;

    public CompiledColorParameter(LyricColorExpression expression)
    {
        _expression = expression;
    }

    public LyricColorValue Evaluate(LyricRenderOperationContext context) =>
        _expression(context.Line, context.Frame, context.Functions);
}

internal sealed class CompiledTextParameter
{
    private readonly LyricTextExpression _expression;

    public CompiledTextParameter(LyricTextExpression expression)
    {
        _expression = expression;
    }

    public string Evaluate(LyricRenderOperationContext context) =>
        _expression(context.Line, context.Frame, context.Functions) ?? string.Empty;
}

internal static class LyricOperationCompilerHelpers
{
    public static CompiledScalarParameter? CompileScalar(
        ILyricExpressionCompiler compiler,
        LyricRenderOperationDefinition definition,
        LyricOperationParameterDescriptor descriptor,
        List<LyricProfileDiagnostic> diagnostics)
    {
        var parameter = GetParameter(definition, descriptor);
        var result = compiler.CompileScalar(parameter.Expression);
        if (!result.IsSuccess)
        {
            diagnostics.Add(ToDiagnostic(definition, descriptor.Key, result.Diagnostic!));
            return null;
        }

        return new CompiledScalarParameter(result.Expression!, parameter.Transition, descriptor.Minimum, descriptor.Maximum);
    }

    public static CompiledColorParameter? CompileColor(
        ILyricExpressionCompiler compiler,
        LyricRenderOperationDefinition definition,
        LyricOperationParameterDescriptor descriptor,
        List<LyricProfileDiagnostic> diagnostics)
    {
        var parameter = GetParameter(definition, descriptor);
        var result = compiler.CompileColor(parameter.Expression);
        if (!result.IsSuccess)
        {
            diagnostics.Add(ToDiagnostic(definition, descriptor.Key, result.Diagnostic!));
            return null;
        }

        return new CompiledColorParameter(result.Expression!);
    }

    public static CompiledTextParameter? CompileText(
        ILyricExpressionCompiler compiler,
        LyricRenderOperationDefinition definition,
        string expression,
        string parameter,
        List<LyricProfileDiagnostic> diagnostics)
    {
        var result = compiler.CompileText(expression);
        if (!result.IsSuccess)
        {
            diagnostics.Add(ToDiagnostic(definition, parameter, result.Diagnostic!));
            return null;
        }

        return new CompiledTextParameter(result.Expression!);
    }

    private static LyricOperationParameterDefinition GetParameter(
        LyricRenderOperationDefinition definition,
        LyricOperationParameterDescriptor descriptor)
    {
        return definition.Parameters.TryGetValue(descriptor.Key, out var parameter)
            ? parameter
            : new LyricOperationParameterDefinition
            {
                Expression = descriptor.DefaultExpression,
                Transition = descriptor.SupportsTransition ? new LyricTransitionDefinition() : null
            };
    }

    private static LyricProfileDiagnostic ToDiagnostic(
        LyricRenderOperationDefinition definition,
        string parameter,
        LyricExpressionDiagnostic diagnostic) =>
        new(
            LyricProfileDiagnosticSeverity.Error,
            diagnostic.Message,
            definition.InstanceId,
            parameter,
            diagnostic.Line,
            diagnostic.Column);
}

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

    private static double GetArgument(LyricTransitionDefinition transition, string key, double fallback) =>
        transition.Arguments.TryGetValue(key, out var value) ? value : fallback;
}

internal sealed class LinearEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime) => normalizedTime;
}
