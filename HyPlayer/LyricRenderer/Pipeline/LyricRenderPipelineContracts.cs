using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HyPlayer.LyricRenderer.Pipeline;

public enum LyricProfileDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record LyricProfileDiagnostic(
    LyricProfileDiagnosticSeverity Severity,
    string Message,
    string? InstanceId = null,
    string? Parameter = null,
    int Line = 0,
    int Column = 0);

public sealed class LyricProfileCompileResult
{
    public required IReadOnlyList<LyricProfileDiagnostic> Diagnostics { get; init; }

    public CompiledLyricEffectProfile? Profile { get; init; }

    public bool IsSuccess => Profile is not null && Diagnostics.All(item => item.Severity != LyricProfileDiagnosticSeverity.Error);
}

public sealed class CompiledLyricEffectProfile
{
    internal CompiledLyricEffectProfile(
        int version,
        LyricEffectProfileDocument document,
        IReadOnlyList<CompiledLyricRenderOperation> operations,
        CompiledFocusedTextEffectProfile focusedText)
    {
        Version = version;
        Document = document;
        Operations = operations;
        FocusedText = focusedText;
    }

    public int Version { get; }

    public LyricEffectProfileDocument Document { get; }

    internal IReadOnlyList<CompiledLyricRenderOperation> Operations { get; }

    public CompiledFocusedTextEffectProfile FocusedText { get; }

    internal LyricRenderPipelineInstance CreatePipeline() =>
        new(Version, Operations.Select(operation => operation.Create()).ToList());
}

public sealed class CompiledLyricRenderOperation
{
    public required LyricRenderOperationDefinition Definition { get; init; }

    public required Func<ILyricRenderOperation> Create { get; init; }
}

public sealed class LyricOperationCompileResult
{
    public CompiledLyricRenderOperation? Operation { get; init; }

    public IReadOnlyList<LyricProfileDiagnostic> Diagnostics { get; init; } = [];

    public bool IsSuccess => Operation is not null && Diagnostics.All(item => item.Severity != LyricProfileDiagnosticSeverity.Error);
}

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

public readonly record struct LyricDrawValue(
    LyricExpressionValueType Type,
    float Scalar,
    LyricColorValue Color,
    string? Text)
{
    public static LyricDrawValue FromScalar(float value) =>
        new(LyricExpressionValueType.Scalar, value, default, null);

    public static LyricDrawValue FromColor(LyricColorValue value) =>
        new(LyricExpressionValueType.Color, 0, value, null);

    public static LyricDrawValue FromText(string value) =>
        new(LyricExpressionValueType.Text, 0, default, value);
}

public interface ILyricDrawCommandFactory
{
    LyricDrawCommandSignature Signature { get; }

    void Execute(LyricDrawExecutionContext context, IReadOnlyList<LyricDrawValue> arguments);
}

public sealed class LyricDrawExecutionContext
{
    private readonly Stack<System.Numerics.Matrix3x2> _transforms = new();

    internal LyricDrawExecutionContext(CanvasDrawingSession session)
    {
        Session = session;
    }

    public CanvasDrawingSession Session { get; }

    public void Save() => _transforms.Push(Session.Transform);

    public void Restore()
    {
        if (_transforms.Count == 0) throw new InvalidOperationException("Restore 没有匹配的 Save。");
        Session.Transform = _transforms.Pop();
    }

    internal void EnsureBalanced()
    {
        if (_transforms.Count != 0) throw new InvalidOperationException("绘图脚本中的 Save/Restore 不平衡。");
    }
}

public sealed class LyricRenderFrameResourceScope : IDisposable
{
    private readonly List<IDisposable> _resources = [];

    public T Track<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        for (var index = _resources.Count - 1; index >= 0; index--)
            _resources[index].Dispose();
        _resources.Clear();
    }
}

public sealed class LyricRenderOperationContext
{
    public ICanvasImage SourceImage { get; set; } = null!;

    public CanvasDrawingSession TargetSession { get; set; } = null!;

    public LyricRenderFrameResourceScope Resources { get; set; } = null!;

    public LyricExpressionLine Line { get; set; }

    public LyricExpressionFrame Frame { get; set; }

    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    public bool DebugEnabled { get; set; }

    public Windows.Foundation.Rect GeometryBounds { get; set; }

    public bool HasContent { get; set; }

    public LyricExpressionFunctions Functions => LyricExpressionFunctions.Instance;
}

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

    public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
    {
        var result = source;
        foreach (var operation in _operations)
        {
            try
            {
                result = operation.Apply(result, context);
            }
            catch (Exception exception)
            {
                if (_reportedFailures.Add(operation))
                    Debug.WriteLine($"Lyric render operation {operation.GetType().Name} failed: {exception}");
            }
        }

        return result;
    }

    public void Dispose()
    {
        foreach (var operation in _operations) operation.Dispose();
    }
}
