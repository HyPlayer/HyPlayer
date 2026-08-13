using System;
using System.Collections.Generic;
using HyPlayer.LyricEffects.Models;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Pipeline;

public interface ILyricRenderOperation : IDisposable
{
    ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context);
}

public interface ILyricRenderOperationFactory
{
    LyricRenderOperationDescriptor Descriptor { get; }

    LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition);
}

public interface ILyricRenderOperationRegistry
{
    IReadOnlyList<LyricRenderOperationDescriptor> Descriptors { get; }

    void Register(ILyricRenderOperationFactory factory);

    LyricProfileCompileResult Compile(LyricEffectProfileDocument document);
}
