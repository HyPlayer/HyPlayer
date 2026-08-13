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
            var value = opacity.Evaluate(context);
            if (value >= 1) return source;
            _effect.Source = source;
            _effect.Opacity = value;
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
            var value = amount.Evaluate(context);
            if (value <= 0) return source;
            _effect.Source = source;
            _effect.BlurAmount = value;
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
            var blur = _blur.Evaluate(context);
            var color = _color.Evaluate(context);
            var opacity = _opacity.Evaluate(context);
            var x = _x.Evaluate(context);
            var y = _y.Evaluate(context);
            if (opacity <= 0 || color.A == 0) return source;

            _shadow.Source = source;
            _shadow.BlurAmount = blur;
            _shadow.ShadowColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
            _shadowOpacity.Opacity = opacity;

            var commandList = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var drawingSession = commandList.CreateDrawingSession();
            drawingSession.DrawImage(_shadowOpacity, x, y);
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
            var x = parameters[0].Evaluate(context);
            var y = parameters[1].Evaluate(context);
            var scaleX = parameters[2].Evaluate(context);
            var scaleY = parameters[3].Evaluate(context);
            var rotation = parameters[4].Evaluate(context);
            var anchor = new Vector2(parameters[5].Evaluate(context), parameters[6].Evaluate(context));
            if (x == 0 && y == 0 && scaleX == 1 && scaleY == 1 && rotation == 0) return source;

            _effect.Source = source;
            _effect.TransformMatrix =
                Matrix3x2.CreateTranslation(-anchor) *
                Matrix3x2.CreateScale(scaleX, scaleY) *
                Matrix3x2.CreateRotation(MathF.PI * rotation / 180f) *
                Matrix3x2.CreateTranslation(anchor + new Vector2(x, y));
            context.GeometryBounds = TransformBounds(context.GeometryBounds, _effect.TransformMatrix);
            return _effect;
        }

        private static Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect bounds, Matrix3x2 matrix)
        {
            var topLeft = Vector2.Transform(new Vector2((float)bounds.Left, (float)bounds.Top), matrix);
            var topRight = Vector2.Transform(new Vector2((float)bounds.Right, (float)bounds.Top), matrix);
            var bottomLeft = Vector2.Transform(new Vector2((float)bounds.Left, (float)bounds.Bottom), matrix);
            var bottomRight = Vector2.Transform(new Vector2((float)bounds.Right, (float)bounds.Bottom), matrix);
            var left = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomLeft.X, bottomRight.X));
            var top = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomLeft.Y, bottomRight.Y));
            var right = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomLeft.X, bottomRight.X));
            var bottom = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomLeft.Y, bottomRight.Y));
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
            var angleX = parameters[0].Evaluate(context);
            var angleY = parameters[1].Evaluate(context);
            var angleZ = parameters[2].Evaluate(context);
            var depth = parameters[3].Evaluate(context);
            var center = new Vector3(parameters[4].Evaluate(context), parameters[5].Evaluate(context), 0);
            if (angleX == 0 && angleY == 0 && angleZ == 0) return source;

            var perspective = Matrix4x4.Identity;
            perspective.M34 = 1f / depth;
            _effect.Source = source;
            _effect.TransformMatrix =
                Matrix4x4.CreateTranslation(-center) *
                Matrix4x4.CreateRotationX(MathF.PI * angleX / 180f) *
                Matrix4x4.CreateRotationY(MathF.PI * angleY / 180f) *
                Matrix4x4.CreateRotationZ(MathF.PI * angleZ / 180f) *
                perspective *
                Matrix4x4.CreateTranslation(center);
            return _effect;
        }

        public void Dispose() => _effect.Dispose();
    }
}
