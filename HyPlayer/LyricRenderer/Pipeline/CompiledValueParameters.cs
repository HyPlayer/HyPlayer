using HyPlayer.LyricEffects.Expressions;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed class CompiledColorParameter
{
    private readonly LyricColorExpression _expression;

    public CompiledColorParameter(LyricColorExpression expression)
    {
        _expression = expression;
    }

    public LyricColorValue Evaluate(LyricRenderOperationContext context)
    {
        return _expression(context.Line, context.Frame, context.Functions);
    }
}

internal sealed class CompiledTextParameter
{
    private readonly LyricTextExpression _expression;

    public CompiledTextParameter(LyricTextExpression expression)
    {
        _expression = expression;
    }

    public string Evaluate(LyricRenderOperationContext context)
    {
        return _expression(context.Line, context.Frame, context.Functions) ?? string.Empty;
    }
}
