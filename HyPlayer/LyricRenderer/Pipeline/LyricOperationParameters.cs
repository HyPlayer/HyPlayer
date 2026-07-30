using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed class CompiledScalarParameter
{
    private readonly LyricScalarExpression _expression;
    private readonly CompiledLyricTransition? _transition;
    private readonly float? _minimum;
    private readonly float? _maximum;

    public CompiledScalarParameter(
        LyricScalarExpression expression,
        CompiledLyricTransition? transition,
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
    private readonly LyricTransitionRuntime? _transition;
    private readonly float? _minimum;
    private readonly float? _maximum;

    public ScalarParameterRuntime(
        LyricScalarExpression expression,
        CompiledLyricTransition? transition,
        float? minimum,
        float? maximum)
    {
        _expression = expression;
        _minimum = minimum;
        _maximum = maximum;
        _transition = transition?.CreateRuntime();
    }

    public float Evaluate(LyricRenderOperationContext context)
    {
        var value = _expression(context.Line, context.Frame, context.Functions);
        if (!float.IsFinite(value)) throw new InvalidOperationException("表达式返回了 NaN 或 Infinity。");
        if (_minimum is { } minimum) value = Math.Max(value, minimum);
        if (_maximum is { } maximum) value = Math.Min(value, maximum);
        return _transition?.Animate(context, value) ?? value;
    }
}

internal sealed class CompiledColorParameter
{
    private readonly LyricColorExpression _expression;
    private readonly CompiledLyricTransition? _transition;

    public CompiledColorParameter(LyricColorExpression expression, CompiledLyricTransition? transition)
    {
        _expression = expression;
        _transition = transition;
    }

    public ColorParameterRuntime CreateRuntime() => new(_expression, _transition);
}

internal sealed class ColorParameterRuntime
{
    private readonly LyricColorExpression _expression;
    private readonly LyricTransitionRuntime? _transition;

    public ColorParameterRuntime(
        LyricColorExpression expression,
        CompiledLyricTransition? transition)
    {
        _expression = expression;
        _transition = transition?.CreateRuntime();
    }

    public LyricColorValue Evaluate(LyricRenderOperationContext context)
    {
        var value = _expression(context.Line, context.Frame, context.Functions);
        return _transition?.Animate(context, value) ?? value;
    }
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

        var transition = CompileTransition(compiler, definition, descriptor.Key, parameter.Transition, diagnostics);
        return new CompiledScalarParameter(result.Expression!, transition, descriptor.Minimum, descriptor.Maximum);
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

        var transition = CompileTransition(compiler, definition, descriptor.Key, parameter.Transition, diagnostics);
        return new CompiledColorParameter(result.Expression!, transition);
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
                Expression = descriptor.DefaultExpression
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

    private static CompiledLyricTransition? CompileTransition(
        ILyricExpressionCompiler compiler,
        LyricRenderOperationDefinition definition,
        string parameter,
        LyricTransitionDefinition? transition,
        List<LyricProfileDiagnostic> diagnostics)
    {
        if (transition is null) return null;
        var duration = compiler.CompileScalar(transition.DurationMs);
        if (!duration.IsSuccess)
        {
            diagnostics.Add(ToDiagnostic(definition, $"{parameter}.transition.durationMs", duration.Diagnostic!));
            return null;
        }

        var arguments = new Dictionary<string, LyricScalarExpression>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, source) in transition.Arguments)
        {
            var result = compiler.CompileScalar(source);
            if (!result.IsSuccess)
                diagnostics.Add(ToDiagnostic(definition, $"{parameter}.transition.arguments.{key}", result.Diagnostic!));
            else
                arguments[key] = result.Expression!;
        }

        return new CompiledLyricTransition(duration.Expression!, transition.EasingId, transition.Mode, arguments);
    }
}

internal sealed class CompiledLyricTransition(
    LyricScalarExpression duration,
    string easingId,
    string mode,
    IReadOnlyDictionary<string, LyricScalarExpression> arguments)
{
    public LyricTransitionRuntime CreateRuntime() => new(duration, easingId, mode, arguments);
}

internal sealed class LyricTransitionRuntime(
    LyricScalarExpression duration,
    string easingId,
    string mode,
    IReadOnlyDictionary<string, LyricScalarExpression> arguments)
{
    private bool _scalarInitialized;
    private float _scalarStart;
    private float _scalarTarget;
    private long _scalarStartTime;
    private TransitionSnapshot _scalarSnapshot;
    private bool _colorInitialized;
    private LyricColorValue _colorStart;
    private LyricColorValue _colorTarget;
    private long _colorStartTime;
    private TransitionSnapshot _colorSnapshot;

    public float Animate(LyricRenderOperationContext context, float target)
    {
        if (!_scalarInitialized)
        {
            _scalarInitialized = true;
            _scalarStart = _scalarTarget = target;
            _scalarStartTime = context.Frame.CurrentTimeMs;
            _scalarSnapshot = Snapshot(context);
            return target;
        }

        if (target != _scalarTarget)
        {
            _scalarStart = Interpolate(_scalarStart, _scalarTarget, Progress(context.Frame.CurrentTimeMs, _scalarStartTime, _scalarSnapshot));
            _scalarTarget = target;
            _scalarStartTime = context.Frame.CurrentTimeMs;
            _scalarSnapshot = Snapshot(context);
        }

        return Interpolate(_scalarStart, _scalarTarget, Progress(context.Frame.CurrentTimeMs, _scalarStartTime, _scalarSnapshot));
    }

    public LyricColorValue Animate(LyricRenderOperationContext context, LyricColorValue target)
    {
        if (!_colorInitialized)
        {
            _colorInitialized = true;
            _colorStart = _colorTarget = target;
            _colorStartTime = context.Frame.CurrentTimeMs;
            _colorSnapshot = Snapshot(context);
            return target;
        }

        if (target != _colorTarget)
        {
            _colorStart = Interpolate(_colorStart, _colorTarget, Progress(context.Frame.CurrentTimeMs, _colorStartTime, _colorSnapshot));
            _colorTarget = target;
            _colorStartTime = context.Frame.CurrentTimeMs;
            _colorSnapshot = Snapshot(context);
        }

        return Interpolate(_colorStart, _colorTarget, Progress(context.Frame.CurrentTimeMs, _colorStartTime, _colorSnapshot));
    }

    private TransitionSnapshot Snapshot(LyricRenderOperationContext context)
    {
        var durationMs = duration(context.Line, context.Frame, context.Functions);
        var values = arguments.ToDictionary(
            pair => pair.Key,
            pair => (double)pair.Value(context.Line, context.Frame, context.Functions),
            StringComparer.OrdinalIgnoreCase);
        return new TransitionSnapshot(durationMs, LyricEasingFactory.Create(easingId, mode, values));
    }

    private static double Progress(long time, long startTime, TransitionSnapshot snapshot) =>
        snapshot.DurationMs <= 0 ? 1 : snapshot.Easing.Ease(Math.Clamp((time - startTime) / snapshot.DurationMs, 0, 1));

    private static float Interpolate(float start, float end, double progress) =>
        (float)(start + (end - start) * progress);

    private static LyricColorValue Interpolate(LyricColorValue start, LyricColorValue end, double progress) => new(
        Channel(start.A, end.A, progress),
        Channel(start.R, end.R, progress),
        Channel(start.G, end.G, progress),
        Channel(start.B, end.B, progress));

    private static byte Channel(byte start, byte end, double progress) =>
        (byte)Math.Clamp(Math.Round(start + (end - start) * progress), byte.MinValue, byte.MaxValue);

    private readonly record struct TransitionSnapshot(double DurationMs, EaseFunctionBase Easing);
}

internal static class LyricEasingFactory
{
    public static EaseFunctionBase Create(
        string easingId,
        string mode,
        IReadOnlyDictionary<string, double> arguments)
    {
        EaseFunctionBase easing = easingId.ToLowerInvariant() switch
        {
            "linear" => new LinearEase(),
            "sine" => new CustomSineEase(),
            "exponential" => new CustomExponentialEase
            {
                Exponent = GetArgument(arguments, "exponent", 2)
            },
            "elastic" => new CustomElasticEase
            {
                Springiness = (float)GetArgument(arguments, "springiness", 6),
                Oscillations = (float)GetArgument(arguments, "oscillations", 1)
            },
            "bounce" => new CustomBounceEase
            {
                Bounces = (int)GetArgument(arguments, "bounces", 3),
                Bounciness = GetArgument(arguments, "bounciness", 2)
            },
            _ => new CustomCircleEase()
        };
        easing.EasingMode = mode.ToLowerInvariant() switch
        {
            "in" => EasingMode.EaseIn,
            "inout" => EasingMode.EaseInOut,
            _ => EasingMode.EaseOut
        };
        return easing;
    }

    private static double GetArgument(IReadOnlyDictionary<string, double> arguments, string key, double fallback) =>
        arguments.TryGetValue(key, out var value) ? value : fallback;
}

internal sealed class LinearEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime) => normalizedTime;
}
