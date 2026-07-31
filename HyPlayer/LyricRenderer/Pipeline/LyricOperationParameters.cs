using System.Collections.Generic;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricRenderer.Pipeline;

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

        return new CompiledScalarParameter(result.Expression!, parameter.Transition, descriptor.Minimum,
            descriptor.Maximum);
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
        LyricExpressionDiagnostic diagnostic)
    {
        return new LyricProfileDiagnostic(
            LyricProfileDiagnosticSeverity.Error,
            diagnostic.Message,
            definition.InstanceId,
            parameter,
            diagnostic.Line,
            diagnostic.Column);
    }
}
