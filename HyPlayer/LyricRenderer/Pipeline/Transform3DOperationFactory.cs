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

internal sealed partial class Transform3DOperationFactory(ILyricExpressionCompiler compiler)
    : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor[] _parameterDescriptors =
    [
        Scalar("angleX", "X 角度", "0"),
        Scalar("angleY", "Y 角度", "0"),
        Scalar("angleZ", "Z 角度", "0"),
        Scalar("depth", "景深", "3000", 1, 100000),
        Scalar("anchorX", "X 锚点", "line.AnchorX"),
        Scalar("anchorY", "Y 锚点", "line.AnchorY")
    ];

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Transform3D,
        DisplayName = "3D 变换",
        Description = "围绕锚点应用三轴旋转和透视。",
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
                : () => new Transform3DOperation(parameters.Select(item => item!.CreateRuntime()).ToArray()));
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

    private sealed partial class Transform3DOperation(ScalarParameterRuntime[] parameters) : ILyricRenderOperation
    {
        private readonly Transform3DEffect _effect = new();

        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            var values = parameters.Select(parameter => parameter.Evaluate(context)).ToArray();
            var center = new Vector3(values[4], values[5], 0);
            var perspective = Matrix4x4.Identity;
            perspective.M34 = 1f / values[3];
            _effect.Source = source;
            _effect.TransformMatrix =
                Matrix4x4.CreateTranslation(-center) *
                Matrix4x4.CreateRotationX(MathF.PI * values[0] / 180f) *
                Matrix4x4.CreateRotationY(MathF.PI * values[1] / 180f) *
                Matrix4x4.CreateRotationZ(MathF.PI * values[2] / 180f) *
                perspective *
                Matrix4x4.CreateTranslation(center);
            return _effect;
        }

        public void Dispose()
        {
            _effect.Dispose();
        }
    }
}
