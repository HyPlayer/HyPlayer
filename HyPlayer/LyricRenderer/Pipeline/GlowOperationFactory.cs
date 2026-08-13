using System.Collections.Generic;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed partial class GlowOperationFactory(ILyricExpressionCompiler compiler)
    : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor _blur = new()
    {
        Key = "blur",
        DisplayName = "辉光半径",
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0",
        SupportsTransition = true,
        Minimum = 0,
        Maximum = 250
    };

    private static readonly LyricOperationParameterDescriptor _opacity = new()
    {
        Key = "opacity",
        DisplayName = "辉光透明度",
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0.4",
        SupportsTransition = true,
        Minimum = 0,
        Maximum = 1
    };

    private static readonly LyricOperationParameterDescriptor _color = new()
    {
        Key = "color",
        DisplayName = "辉光颜色",
        ValueType = LyricExpressionValueType.Color,
        DefaultExpression = "line.AccentColor"
    };

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Glow,
        DisplayName = "整体辉光",
        Description = "在歌词图像后合成带颜色的阴影辉光。",
        Parameters = (LyricOperationParameterDescriptor[])[_blur, _opacity, _color]
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var blur = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, _blur, diagnostics);
        var opacity = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, _opacity, diagnostics);
        var color = LyricOperationCompilerHelpers.CompileColor(Compiler, definition, _color, diagnostics);
        return Result(
            definition,
            diagnostics,
            blur is null || opacity is null || color is null
                ? null
                : () => new GlowOperation(blur.CreateRuntime(), opacity.CreateRuntime(), color));
    }

    private sealed partial class GlowOperation : ILyricRenderOperation
    {
        private readonly ScalarParameterRuntime _blur;
        private readonly CompiledColorParameter _color;
        private readonly ScalarParameterRuntime _opacity;
        private readonly ShadowEffect _shadow = new();
        private readonly OpacityEffect _shadowOpacity = new();

        public GlowOperation(
            ScalarParameterRuntime blur,
            ScalarParameterRuntime opacity,
            CompiledColorParameter color)
        {
            _blur = blur;
            _opacity = opacity;
            _color = color;
            _shadowOpacity.Source = _shadow;
        }

        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            _shadow.Source = source;
            _shadow.BlurAmount = _blur.Evaluate(context);
            var color = _color.Evaluate(context);
            _shadow.ShadowColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
            _shadowOpacity.Opacity = _opacity.Evaluate(context);

            var commandList = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var drawingSession = commandList.CreateDrawingSession();
            drawingSession.DrawImage(_shadowOpacity);
            drawingSession.DrawImage(source);
            return commandList;
        }

        public void Dispose()
        {
            _shadowOpacity.Dispose();
            _shadow.Dispose();
        }
    }
}
