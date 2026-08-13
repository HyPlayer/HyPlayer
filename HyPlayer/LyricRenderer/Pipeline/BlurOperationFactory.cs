using System.Collections.Generic;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed partial class BlurOperationFactory(ILyricExpressionCompiler compiler)
    : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor _amount = new()
    {
        Key = "amount",
        DisplayName = "模糊量",
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0",
        SupportsTransition = true,
        Minimum = 0,
        Maximum = 250
    };

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.GaussianBlur,
        DisplayName = "高斯模糊",
        Description = "对当前合成图像应用 Win2D 高斯模糊。",
        Parameters = (LyricOperationParameterDescriptor[])[_amount]
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var amount = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, _amount, diagnostics);
        return Result(definition, diagnostics, amount is null ? null : () => new BlurOperation(amount.CreateRuntime()));
    }

    private sealed partial class BlurOperation(ScalarParameterRuntime amount) : ILyricRenderOperation
    {
        private readonly GaussianBlurEffect _effect = new();

        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            _effect.Source = source;
            _effect.BlurAmount = amount.Evaluate(context);
            return _effect;
        }

        public void Dispose()
        {
            _effect.Dispose();
        }
    }
}
