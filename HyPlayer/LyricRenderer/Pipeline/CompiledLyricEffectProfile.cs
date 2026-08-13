using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricRenderer.Pipeline;

public sealed class CompiledLyricEffectProfile
{
    internal CompiledLyricEffectProfile(
        int version,
        LyricEffectProfileDocument document,
        IReadOnlyList<CompiledLyricRenderOperation> operations)
    {
        Version = version;
        Document = document;
        Operations = operations;
    }

    public int Version { get; }

    public LyricEffectProfileDocument Document { get; }

    internal IReadOnlyList<CompiledLyricRenderOperation> Operations { get; }

    internal LyricRenderPipelineInstance CreatePipeline()
    {
        return new LyricRenderPipelineInstance(Version, Operations.Select(operation => operation.Create()).ToList());
    }
}

public sealed class CompiledLyricRenderOperation
{
    public required LyricRenderOperationDefinition Definition { get; init; }

    public required Func<ILyricRenderOperation> Create { get; init; }
}
