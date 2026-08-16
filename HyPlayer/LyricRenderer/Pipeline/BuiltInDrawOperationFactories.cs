using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricEffects.Expressions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed partial class BackgroundDrawOperationFactory(ILyricExpressionCompiler compiler) : ExpressionOperationFactoryBase(compiler)
{
    private static readonly LyricOperationParameterDescriptor[] ParameterDescriptors =
    [
        Parameter("color", "背景颜色", LyricExpressionValueType.Color, "fx.Rgba(255, 255, 255, 0.04)"),
        Parameter("opacity", "透明度", LyricExpressionValueType.Scalar, "1", 0, 1),
        Parameter("marginX", "水平边距", LyricExpressionValueType.Scalar, "0"),
        Parameter("marginY", "垂直边距", LyricExpressionValueType.Scalar, "0"),
        Parameter("cornerRadius", "圆角半径", LyricExpressionValueType.Scalar, "6", 0)
    ];

    public override LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Background,
        DisplayName = "背景",
        Description = "在当前输入后方绘制背景；几何边界跟随前序变换，不吸收模糊或辉光外扩。",
        Parameters = ParameterDescriptors,
        Category = LyricRenderOperationCategory.Draw
    };

    public override LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var color = LyricOperationCompilerHelpers.CompileColor(Compiler, definition, ParameterDescriptors[0], diagnostics);
        var scalars = ParameterDescriptors.Skip(1)
            .Select(item => LyricOperationCompilerHelpers.CompileScalar(Compiler, definition, item, diagnostics))
            .ToArray();
        return Result(definition, diagnostics,
            color is null || scalars.Any(item => item is null)
                ? null
                : () => new BackgroundDrawOperation(
                    color.CreateRuntime(), scalars.Select(item => item!.CreateRuntime()).ToArray()));
    }

    private static LyricOperationParameterDescriptor Parameter(
        string key,
        string name,
        LyricExpressionValueType type,
        string expression,
        float? minimum = null,
        float? maximum = null) => new()
    {
        Key = key,
        DisplayName = name,
        ValueType = type,
        DefaultExpression = expression,
        SupportsTransition = true,
        Minimum = minimum,
        Maximum = maximum
    };

    private sealed partial class BackgroundDrawOperation(
        ColorParameterRuntime color,
        ScalarParameterRuntime[] scalars) : ILyricRenderOperation
    {
        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            var opacity = scalars[0].Evaluate(context);
            var marginX = scalars[1].Evaluate(context);
            var marginY = scalars[2].Evaluate(context);
            var radius = scalars[3].Evaluate(context);
            var value = color.Evaluate(context);
            var alpha = (byte)Math.Clamp(Math.Round(value.A * opacity), byte.MinValue, byte.MaxValue);
            var bounds = context.GeometryBounds;
            var rectangle = new Windows.Foundation.Rect(
                bounds.X - marginX,
                bounds.Y - marginY,
                Math.Max(0, bounds.Width + marginX * 2),
                Math.Max(0, bounds.Height + marginY * 2));
            if (alpha == 0 || rectangle.Width <= 0 || rectangle.Height <= 0) return source;

            var result = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var session = result.CreateDrawingSession();
            using var geometry = CanvasGeometry.CreateRoundedRectangle(
                context.TargetSession,
                rectangle,
                Math.Max(radius, 0),
                Math.Max(radius, 0));
            session.FillGeometry(geometry, Color.FromArgb(alpha, value.R, value.G, value.B));
            session.DrawImage(source);
            context.HasContent = true;
            return result;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed partial class SourceDrawOperationFactory : ILyricRenderOperationFactory
{
    public LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Source,
        DisplayName = "歌词内容",
        Description = "将当前歌词行的基础图像绘制到操作链中。该内置节点不可编辑，但可以调整顺序。",
        Parameters = [],
        Category = LyricRenderOperationCategory.Draw,
        IsEditable = false,
        IsRequired = true
    };

    public LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition) => new()
    {
        Operation = new CompiledLyricRenderOperation
        {
            Definition = definition,
            Create = static () => new SourceDrawOperation()
        }
    };

    private sealed partial class SourceDrawOperation : ILyricRenderOperation
    {
        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            if (!context.HasContent)
            {
                context.HasContent = true;
                return context.SourceImage;
            }

            var commandList = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var session = commandList.CreateDrawingSession();
            session.DrawImage(source);
            session.DrawImage(context.SourceImage);
            context.HasContent = true;
            return commandList;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed partial class DebugDrawOperationFactory : ILyricRenderOperationFactory
{
    public LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.Debug,
        DisplayName = "Debug 信息",
        Description = "在启用歌词调试时绘制行坐标、索引与边界。该内置节点不可编辑，但可以调整顺序。",
        Parameters = [],
        Category = LyricRenderOperationCategory.Draw,
        IsEditable = false,
        IsRequired = true
    };

    public LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition) => new()
    {
        Operation = new CompiledLyricRenderOperation
        {
            Definition = definition,
            Create = static () => new DebugDrawOperation()
        }
    };

    private sealed partial class DebugDrawOperation : ILyricRenderOperation
    {
        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            if (!context.DebugEnabled) return source;

            var commandList = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var session = commandList.CreateDrawingSession();
            session.DrawImage(source);
            session.DrawText(
                $"(X{context.OffsetX},Y{context.OffsetY},W{context.Line.Width},H{context.Line.Height})",
                0,
                0,
                Colors.Red);
            session.DrawText(context.Line.Index.ToString(), 0, 15, Colors.Red);
            session.DrawRectangle(0, 0, context.Line.Width, context.Line.Height, Colors.Yellow);
            context.HasContent = true;
            return commandList;
        }

        public void Dispose()
        {
        }
    }
}
