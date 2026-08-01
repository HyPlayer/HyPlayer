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
    private readonly LyricScalarExpression? _expression;
    private readonly float _constantValue;
    private readonly CompiledLyricTransition? _transition;
    private readonly float? _minimum;
    private readonly float? _maximum;

    public CompiledScalarParameter(
        LyricScalarExpression expression,
        FocusedTextExpressionDependencies dependencies,
        CompiledLyricTransition? transition,
        float? minimum,
        float? maximum)
    {
        _minimum = minimum;
        _maximum = maximum;
        if (dependencies == FocusedTextExpressionDependencies.None)
        {
            var sample = LyricExpressionSamples.All[0];
            _constantValue = Clamp(expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance));
            _transition = null;
        }
        else
        {
            _expression = expression;
            _transition = transition;
        }
    }

    public ScalarParameterRuntime CreateRuntime() =>
        new(_expression, _constantValue, _transition, _minimum, _maximum);

    private float Clamp(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException("表达式返回了 NaN 或 Infinity。");
        if (_minimum is { } minimum) value = Math.Max(value, minimum);
        if (_maximum is { } maximum) value = Math.Min(value, maximum);
        return value;
    }
}

internal sealed class ScalarParameterRuntime
{
    private readonly LyricScalarExpression? _expression;
    private readonly float _constantValue;
    private readonly LyricTransitionRuntime? _transition;
    private readonly float? _minimum;
    private readonly float? _maximum;

    public ScalarParameterRuntime(
        LyricScalarExpression? expression,
        float constantValue,
        CompiledLyricTransition? transition,
        float? minimum,
        float? maximum)
    {
        _expression = expression;
        _constantValue = constantValue;
        _minimum = minimum;
        _maximum = maximum;
        _transition = transition?.CreateRuntime();
    }

    public float Evaluate(LyricRenderOperationContext context)
    {
        if (_expression is null) return _constantValue;
        var value = _expression(context.Line, context.Frame, context.Functions);
        if (!float.IsFinite(value)) throw new InvalidOperationException("表达式返回了 NaN 或 Infinity。");
        if (_minimum is { } minimum) value = Math.Max(value, minimum);
        if (_maximum is { } maximum) value = Math.Min(value, maximum);
        return _transition?.Animate(context, value) ?? value;
    }
}

internal sealed class CompiledColorParameter
{
    private readonly LyricColorExpression? _expression;
    private readonly LyricColorValue _constantValue;
    private readonly CompiledLyricTransition? _transition;

    public CompiledColorParameter(
        LyricColorExpression expression,
        FocusedTextExpressionDependencies dependencies,
        CompiledLyricTransition? transition)
    {
        if (dependencies == FocusedTextExpressionDependencies.None)
        {
            var sample = LyricExpressionSamples.All[0];
            _constantValue = expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance);
        }
        else
        {
            _expression = expression;
            _transition = transition;
        }
    }

    public ColorParameterRuntime CreateRuntime() => new(_expression, _constantValue, _transition);
}

internal sealed class ColorParameterRuntime
{
    private readonly LyricColorExpression? _expression;
    private readonly LyricColorValue _constantValue;
    private readonly LyricTransitionRuntime? _transition;

    public ColorParameterRuntime(
        LyricColorExpression? expression,
        LyricColorValue constantValue,
        CompiledLyricTransition? transition)
    {
        _expression = expression;
        _constantValue = constantValue;
        _transition = transition?.CreateRuntime();
    }

    public LyricColorValue Evaluate(LyricRenderOperationContext context)
    {
        if (_expression is null) return _constantValue;
        var value = _expression(context.Line, context.Frame, context.Functions);
        return _transition?.Animate(context, value) ?? value;
    }
}

internal sealed class CompiledTextParameter
{
    private readonly LyricTextExpression? _expression;
    private readonly string _constantValue;

    public CompiledTextParameter(LyricTextExpression expression, FocusedTextExpressionDependencies dependencies)
    {
        if (dependencies == FocusedTextExpressionDependencies.None)
        {
            var sample = LyricExpressionSamples.All[0];
            _constantValue = expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance) ?? string.Empty;
        }
        else
        {
            _expression = expression;
            _constantValue = string.Empty;
        }
    }

    public string Evaluate(LyricRenderOperationContext context) =>
        _expression is null
            ? _constantValue
            : _expression(context.Line, context.Frame, context.Functions) ?? string.Empty;
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
        return new CompiledScalarParameter(
            result.Expression!, result.Dependencies, transition, descriptor.Minimum, descriptor.Maximum);
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
        return new CompiledColorParameter(result.Expression!, result.Dependencies, transition);
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

        return new CompiledTextParameter(result.Expression!, result.Dependencies);
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

        var arguments = new Dictionary<string, CompiledTransitionScalar>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, source) in transition.Arguments)
        {
            var result = compiler.CompileScalar(source);
            if (!result.IsSuccess)
                diagnostics.Add(ToDiagnostic(definition, $"{parameter}.transition.arguments.{key}", result.Diagnostic!));
            else
                arguments[key] = CompiledTransitionScalar.Create(result);
        }

        return new CompiledLyricTransition(
            CompiledTransitionScalar.Create(duration), transition.EasingId, transition.Mode, arguments);
    }
}

internal sealed class CompiledLyricTransition(
    CompiledTransitionScalar duration,
    string easingId,
    string mode,
    IReadOnlyDictionary<string, CompiledTransitionScalar> arguments)
{
    public LyricTransitionRuntime CreateRuntime() => new(duration, easingId, mode, arguments);
}

internal sealed class LyricTransitionRuntime(
    CompiledTransitionScalar duration,
    string easingId,
    string mode,
    IReadOnlyDictionary<string, CompiledTransitionScalar> arguments)
{
    private readonly CompiledTransitionScalar _exponent = GetArgument(arguments, "exponent", 2);
    private readonly CompiledTransitionScalar _springiness = GetArgument(arguments, "springiness", 6);
    private readonly CompiledTransitionScalar _oscillations = GetArgument(arguments, "oscillations", 1);
    private readonly CompiledTransitionScalar _bounces = GetArgument(arguments, "bounces", 3);
    private readonly CompiledTransitionScalar _bounciness = GetArgument(arguments, "bounciness", 2);
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
        return new TransitionSnapshot(
            duration.Evaluate(context),
            _exponent.Evaluate(context),
            _springiness.Evaluate(context),
            _oscillations.Evaluate(context),
            _bounces.Evaluate(context),
            _bounciness.Evaluate(context));
    }

    private double Progress(long time, long startTime, TransitionSnapshot snapshot)
    {
        if (snapshot.DurationMs <= 0) return 1;
        var progress = Math.Clamp((time - startTime) / snapshot.DurationMs, 0, 1);
        return LyricEasingFactory.Evaluate(
            easingId,
            mode,
            progress,
            snapshot.Exponent,
            snapshot.Springiness,
            snapshot.Oscillations,
            snapshot.Bounces,
            snapshot.Bounciness);
    }

    private static float Interpolate(float start, float end, double progress) =>
        (float)(start + (end - start) * progress);

    private static LyricColorValue Interpolate(LyricColorValue start, LyricColorValue end, double progress) => new(
        Channel(start.A, end.A, progress),
        Channel(start.R, end.R, progress),
        Channel(start.G, end.G, progress),
        Channel(start.B, end.B, progress));

    private static byte Channel(byte start, byte end, double progress) =>
        (byte)Math.Clamp(Math.Round(start + (end - start) * progress), byte.MinValue, byte.MaxValue);

    private static CompiledTransitionScalar GetArgument(
        IReadOnlyDictionary<string, CompiledTransitionScalar> values,
        string key,
        float fallback) => values.TryGetValue(key, out var value)
        ? value
        : CompiledTransitionScalar.Constant(fallback);

    private readonly record struct TransitionSnapshot(
        double DurationMs,
        double Exponent,
        double Springiness,
        double Oscillations,
        double Bounces,
        double Bounciness);
}

internal readonly struct CompiledTransitionScalar
{
    private readonly LyricScalarExpression? _expression;
    private readonly float _constant;

    private CompiledTransitionScalar(LyricScalarExpression? expression, float constant)
    {
        _expression = expression;
        _constant = constant;
    }

    public static CompiledTransitionScalar Create(LyricExpressionCompileResult<LyricScalarExpression> result)
    {
        if (result.Dependencies != FocusedTextExpressionDependencies.None)
            return new CompiledTransitionScalar(result.Expression!, 0);

        var sample = LyricExpressionSamples.All[0];
        return Constant(result.Expression!(sample.Line, sample.Frame, LyricExpressionFunctions.Instance));
    }

    public static CompiledTransitionScalar Constant(float value) => new(null, value);

    public float Evaluate(LyricRenderOperationContext context) => _expression is null
        ? _constant
        : _expression(context.Line, context.Frame, context.Functions);
}

internal static class LyricEasingFactory
{
    public static double Evaluate(
        string easingId,
        string mode,
        double progress,
        double exponent,
        double springiness,
        double oscillations,
        double bounces,
        double bounciness)
    {
        if (mode.Equals("in", StringComparison.OrdinalIgnoreCase))
            return EaseIn(easingId, progress, exponent, springiness, oscillations, bounces, bounciness);
        if (mode.Equals("inout", StringComparison.OrdinalIgnoreCase))
            return progress < 0.5
                ? EaseIn(easingId, progress * 2, exponent, springiness, oscillations, bounces, bounciness) * 0.5
                : (1 - EaseIn(easingId, (1 - progress) * 2, exponent, springiness, oscillations, bounces,
                    bounciness)) * 0.5 + 0.5;
        return 1 - EaseIn(easingId, 1 - progress, exponent, springiness, oscillations, bounces, bounciness);
    }

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

    private static double EaseIn(
        string easingId,
        double progress,
        double exponent,
        double springiness,
        double oscillations,
        double bounces,
        double bounciness)
    {
        if (easingId.Equals("linear", StringComparison.OrdinalIgnoreCase)) return progress;
        if (easingId.Equals("sine", StringComparison.OrdinalIgnoreCase))
        {
            progress = Math.Clamp(progress, 0, 1);
            return 1 - Math.Sin((1 - progress) * Math.PI * 0.5);
        }
        if (easingId.Equals("exponential", StringComparison.OrdinalIgnoreCase))
        {
            if (Math.Abs(exponent) < 0.00001) return progress;
            return (Math.Exp(exponent * progress) - 1) / (Math.Exp(exponent) - 1);
        }
        if (easingId.Equals("elastic", StringComparison.OrdinalIgnoreCase))
            return FocusedTextProgress.GetElasticProgress(progress, springiness, oscillations);
        if (easingId.Equals("bounce", StringComparison.OrdinalIgnoreCase))
            return Bounce(progress, (int)bounces, bounciness);

        progress = Math.Clamp(progress, 0, 1);
        return 1 - Math.Sqrt(1 - progress * progress);
    }

    private static double Bounce(double progress, int bounceCount, double bounciness)
    {
        var bounces = Math.Max(0, bounceCount);
        if (bounciness < 1 || Math.Abs(bounciness - 1) < 2.2204460492503131e-016)
            bounciness = 1.001;
        var power = Math.Pow(bounciness, bounces);
        var oneMinusBounciness = 1 - bounciness;
        var unitCount = (1 - power) / oneMinusBounciness + power * 0.5;
        var unitAtProgress = progress * unitCount;
        var bounceAtProgress = Math.Log(-unitAtProgress * oneMinusBounciness + 1, bounciness);
        var start = Math.Floor(bounceAtProgress);
        var end = start + 1;
        var startTime = (1 - Math.Pow(bounciness, start)) / (oneMinusBounciness * unitCount);
        var endTime = (1 - Math.Pow(bounciness, end)) / (oneMinusBounciness * unitCount);
        var middleTime = (startTime + endTime) * 0.5;
        var relativeToPeak = progress - middleTime;
        var radius = middleTime - startTime;
        var amplitude = Math.Pow(1 / bounciness, bounces - start);
        return (-amplitude / (radius * radius)) * (relativeToPeak - radius) * (relativeToPeak + radius);
    }
}

internal sealed class LinearEase : EaseFunctionBase
{
    protected override double EaseInCore(double normalizedTime) => normalizedTime;
}
