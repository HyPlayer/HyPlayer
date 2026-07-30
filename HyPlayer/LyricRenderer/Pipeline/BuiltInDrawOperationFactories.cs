using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using System;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed class SourceDrawOperationFactory : ILyricRenderOperationFactory
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

    private sealed class SourceDrawOperation : ILyricRenderOperation
    {
        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            var commandList = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var session = commandList.CreateDrawingSession();
            session.DrawImage(source);
            session.DrawImage(context.SourceImage);
            return commandList;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class DebugDrawOperationFactory : ILyricRenderOperationFactory
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

    private sealed class DebugDrawOperation : ILyricRenderOperation
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
            return commandList;
        }

        public void Dispose()
        {
        }
    }
}
