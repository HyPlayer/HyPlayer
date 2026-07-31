using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed partial class Transform2DOperationFactory(ILyricExpressionCompiler compiler)
    : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor[] _parameterDescriptors =
    [
        Scalar("x", "X 位移", "0"),
        Scalar("y", "Y 位移", "0"),
        Scalar("scaleX", "X 缩放", "1", -10, 10),
        Scalar("scaleY", "Y 缩放", "1", -10, 10),
        Scalar("rotation", "旋转角度", "0"),
        Scalar("anchorX", "X 锚点", "line.AnchorX"),
        Scalar("anchorY", "Y 锚点", "line.AnchorY")
    ];

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Transform2D,
        DisplayName = "2D 变换",
        Description = "围绕可配置锚点平移、缩放和旋转。",
        Parameters = _parameterDescriptors
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var parameters = _parameterDescriptors
            .Select(item => LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, item, diagnostics))
            .ToArray();
        return Result(
            definition,
            diagnostics,
            parameters.Any(item => item is null)
                ? null
                : () => new Transform2DOperation(parameters.Select(item => item!.CreateRuntime()).ToArray()));
    }

    private static LyricOperationParameterDescriptor Scalar(
        string key,
        string name,
        string expression,
        float? minimum = null,
        float? maximum = null)
    {
        return new LyricOperationParameterDescriptor
        {
            Key = key,
            DisplayName = name,
            ValueType = LyricExpressionValueType.Scalar,
            DefaultExpression = expression,
            SupportsTransition = true,
            Minimum = minimum,
            Maximum = maximum
        };
    }

    private sealed partial class Transform2DOperation(ScalarParameterRuntime[] parameters) : ILyricRenderOperation
    {
        private readonly Transform2DEffect _effect = new();

        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            var values = parameters.Select(parameter => parameter.Evaluate(context)).ToArray();
            var anchor = new Vector2(values[5], values[6]);
            _effect.Source = source;
            _effect.TransformMatrix =
                Matrix3x2.CreateTranslation(-anchor) *
                Matrix3x2.CreateScale(values[2], values[3]) *
                Matrix3x2.CreateRotation(MathF.PI * values[4] / 180f) *
                Matrix3x2.CreateTranslation(anchor + new Vector2(values[0], values[1]));
            return _effect;
        }

        public void Dispose()
        {
            _effect.Dispose();
        }
    }
}
