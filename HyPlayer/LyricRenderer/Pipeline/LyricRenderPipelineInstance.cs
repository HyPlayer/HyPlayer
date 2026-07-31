using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Pipeline;

internal sealed class LyricRenderPipelineInstance : IDisposable
{
    private readonly IReadOnlyList<ILyricRenderOperation> _operations;
    private readonly HashSet<ILyricRenderOperation> _reportedFailures = [];

    public LyricRenderPipelineInstance(int version, IReadOnlyList<ILyricRenderOperation> operations)
    {
        Version = version;
        _operations = operations;
    }

    public int Version { get; }

    public void Dispose()
    {
        foreach (var operation in _operations) operation.Dispose();
    }

    public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
    {
        var result = source;
        foreach (var operation in _operations)
            try
            {
                result = operation.Apply(result, context);
            }
            catch (Exception exception)
            {
                if (_reportedFailures.Add(operation))
                    Debug.WriteLine($"Lyric render operation {operation.GetType().Name} failed: {exception}");
            }

        return result;
    }
}
