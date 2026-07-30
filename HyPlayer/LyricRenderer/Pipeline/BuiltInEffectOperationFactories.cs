using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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

internal sealed partial class OpacityOperationFactory(ILyricExpressionCompiler compiler) : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor Opacity = new()
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
        Parameters = (LyricOperationParameterDescriptor[])[Opacity]
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var opacity = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, Opacity, diagnostics);
        return Result(definition, diagnostics, opacity is null ? null : () => new OpacityOperation(opacity.CreateRuntime()));
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

        public void Dispose() => _effect.Dispose();
    }
}

internal sealed partial class BlurOperationFactory(ILyricExpressionCompiler compiler) : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor Amount = new()
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
        Parameters = (LyricOperationParameterDescriptor[])[Amount]
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var amount = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, Amount, diagnostics);
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

        public void Dispose() => _effect.Dispose();
    }
}

internal sealed partial class GlowOperationFactory(ILyricExpressionCompiler compiler) : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor X = new()
    {
        Key = "x", DisplayName = "X 偏移", ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0", SupportsTransition = true
    };

    private static readonly LyricOperationParameterDescriptor Y = new()
    {
        Key = "y", DisplayName = "Y 偏移", ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0", SupportsTransition = true
    };

    private static readonly LyricOperationParameterDescriptor Blur = new()
    {
        Key = "blur",
        DisplayName = "辉光半径",
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0",
        SupportsTransition = true,
        Minimum = 0,
        Maximum = 250
    };

    private static readonly LyricOperationParameterDescriptor Opacity = new()
    {
        Key = "opacity",
        DisplayName = "辉光透明度",
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = "0.4",
        SupportsTransition = true,
        Minimum = 0,
        Maximum = 1
    };

    private static readonly LyricOperationParameterDescriptor Color = new()
    {
        Key = "color",
        DisplayName = "辉光颜色",
        ValueType = LyricExpressionValueType.Color,
        DefaultExpression = "line.FocusingColor",
        SupportsTransition = true
    };

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Glow,
        DisplayName = "整体辉光",
        Description = "在歌词图像后合成带颜色的阴影辉光。",
        Parameters = (LyricOperationParameterDescriptor[])[X, Y, Blur, Opacity, Color]
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var x = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, X, diagnostics);
        var y = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, Y, diagnostics);
        var blur = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, Blur, diagnostics);
        var opacity = LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, Opacity, diagnostics);
        var color = LyricOperationCompilerHelpers.CompileColor(Compiler, definition, Color, diagnostics);
        return Result(
            definition,
            diagnostics,
            x is null || y is null || blur is null || opacity is null || color is null
                ? null
                : () => new GlowOperation(
                    x.CreateRuntime(), y.CreateRuntime(), blur.CreateRuntime(), opacity.CreateRuntime(), color.CreateRuntime()));
    }

    private sealed partial class GlowOperation : ILyricRenderOperation
    {
        private readonly ScalarParameterRuntime _x;
        private readonly ScalarParameterRuntime _y;
        private readonly ScalarParameterRuntime _blur;
        private readonly ScalarParameterRuntime _opacity;
        private readonly ColorParameterRuntime _color;
        private readonly ShadowEffect _shadow = new();
        private readonly OpacityEffect _shadowOpacity = new();

        public GlowOperation(
            ScalarParameterRuntime x,
            ScalarParameterRuntime y,
            ScalarParameterRuntime blur,
            ScalarParameterRuntime opacity,
            ColorParameterRuntime color)
        {
            _x = x;
            _y = y;
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
            drawingSession.DrawImage(_shadowOpacity, _x.Evaluate(context), _y.Evaluate(context));
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

internal sealed partial class Transform2DOperationFactory(ILyricExpressionCompiler compiler) : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor[] ParameterDescriptors =
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
        Parameters = ParameterDescriptors
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var parameters = ParameterDescriptors
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
        float? maximum = null) => new()
    {
        Key = key,
        DisplayName = name,
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = expression,
        SupportsTransition = true,
        Minimum = minimum,
        Maximum = maximum
    };

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
            context.GeometryBounds = TransformBounds(context.GeometryBounds, _effect.TransformMatrix);
            return _effect;
        }

        private static Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect bounds, Matrix3x2 matrix)
        {
            var points = new[]
            {
                Vector2.Transform(new Vector2((float)bounds.Left, (float)bounds.Top), matrix),
                Vector2.Transform(new Vector2((float)bounds.Right, (float)bounds.Top), matrix),
                Vector2.Transform(new Vector2((float)bounds.Left, (float)bounds.Bottom), matrix),
                Vector2.Transform(new Vector2((float)bounds.Right, (float)bounds.Bottom), matrix)
            };
            var left = points.Min(point => point.X);
            var top = points.Min(point => point.Y);
            var right = points.Max(point => point.X);
            var bottom = points.Max(point => point.Y);
            return new Windows.Foundation.Rect(left, top, right - left, bottom - top);
        }

        public void Dispose() => _effect.Dispose();
    }
}

internal sealed partial class Transform3DOperationFactory(ILyricExpressionCompiler compiler) : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor[] ParameterDescriptors =
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
        Parameters = ParameterDescriptors
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var parameters = ParameterDescriptors
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
        float? maximum = null) => new()
    {
        Key = key,
        DisplayName = name,
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = expression,
        SupportsTransition = true,
        Minimum = minimum,
        Maximum = maximum
    };

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

        public void Dispose() => _effect.Dispose();
    }
}
