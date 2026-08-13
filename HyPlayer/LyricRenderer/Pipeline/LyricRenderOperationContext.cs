using System;
using System.Collections.Generic;
using HyPlayer.LyricEffects.Expressions;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Pipeline;

public sealed class LyricRenderFrameResourceScope : IDisposable
{
    private readonly List<IDisposable> _resources = [];

    public void Dispose()
    {
        for (var index = _resources.Count - 1; index >= 0; index--)
            _resources[index].Dispose();
        _resources.Clear();
    }

    public T Track<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }
}

public sealed class LyricRenderOperationContext
{
    public required ICanvasImage SourceImage { get; init; }

    public required CanvasDrawingSession TargetSession { get; init; }

    public required LyricRenderFrameResourceScope Resources { get; init; }

    public required LyricExpressionLine Line { get; init; }

    public required LyricExpressionFrame Frame { get; init; }

    public required float OffsetX { get; init; }

    public required float OffsetY { get; init; }

    public required bool DebugEnabled { get; init; }

    public LyricExpressionFunctions Functions => LyricExpressionFunctions.Instance;
}
