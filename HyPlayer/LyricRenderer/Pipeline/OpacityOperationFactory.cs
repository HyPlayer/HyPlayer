using System.Collections.Generic;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed partial class OpacityOperationFactory(ILyricExpressionCompiler compiler)
    : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor _opacity = new()
    {
        Key = "opacity",
        DisplayName = "透明度",
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "1",
        SupportsTransition = true,
        Minimum = 0,
        Maximum = 1
    };

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Opacity,
        DisplayName = "透明度",
        Description = "改变当前歌词行合成结果的透明度。",
        Parameters = (LyricOperationParameterDescriptor[])[_opacity]
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var opacity = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, _opacity, diagnostics);
        return Result(definition, diagnostics,
            opacity is null ? null : () => new OpacityOperation(opacity.CreateRuntime()));
    }

    private sealed partial class OpacityOperation(ScalarParameterRuntime opacity) : ILyricRenderOperation
    {
        private readonly OpacityEffect _effect = new();

        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            _effect.Source = source;
            _effect.Opacity = opacity.Evaluate(context);
            return _effect;
        }

        public void Dispose()
        {
            _effect.Dispose();
        }
    }
}
