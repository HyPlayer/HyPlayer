using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricRenderer.Pipeline;

internal abstract class ExpressionOperationFactoryBase(ILyricExpressionCompiler compiler) : ILyricRenderOperationFactory
{
    protected ILyricExpressionCompiler Compiler { get; } = compiler;

    public abstract LyricRenderOperationDescriptor Descriptor { get; }

    public abstract LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition);

    protected static LyricOperationCompileResult Result(
        LyricRenderOperationDefinition definition,
        List<LyricProfileDiagnostic> diagnostics,
        Func<ILyricRenderOperation>? create)
    {
        return new LyricOperationCompileResult
        {
            Diagnostics = diagnostics,
            Operation = create is null || diagnostics.Any(item => item.Severity == LyricProfileDiagnosticSeverity.Error)
                ? null
                : new CompiledLyricRenderOperation
                {
                    Definition = definition,
                    Create = create
                }
        };
    }
}
