using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;

namespace HyPlayer.LyricRenderer.Pipeline;

public sealed class LyricRenderOperationRegistry : ILyricRenderOperationRegistry
{
    public const int MaximumOperationCount = 64;

    private readonly Dictionary<string, ILyricRenderOperationFactory> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    private int _version;

    public LyricRenderOperationRegistry(
        ILyricExpressionCompiler expressionCompiler,
        ILyricDrawScriptParser drawScriptParser,
        LyricDrawCommandRegistry drawCommands)
    {
        Register(new SourceDrawOperationFactory());
        Register(new DebugDrawOperationFactory());
        Register(new GlowOperationFactory(expressionCompiler));
        Register(new OpacityOperationFactory(expressionCompiler));
        Register(new BlurOperationFactory(expressionCompiler));
        Register(new Transform2DOperationFactory(expressionCompiler));
        Register(new Transform3DOperationFactory(expressionCompiler));
        Register(new DrawScriptOperationFactory(expressionCompiler, drawScriptParser, drawCommands));
    }

    public IReadOnlyList<LyricRenderOperationDescriptor> Descriptors =>
        _factories.Values.Select(factory => factory.Descriptor).OrderBy(item => item.DisplayName).ToList();

    public void Register(ILyricRenderOperationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(factory.Descriptor.TypeId, factory))
            throw new InvalidOperationException($"歌词渲染操作“{factory.Descriptor.TypeId}”已注册。");
    }

    public LyricProfileCompileResult Compile(LyricEffectProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<LyricProfileDiagnostic>();
        if (!string.Equals(document.Format, LyricEffectProfileDocument.CurrentFormat, StringComparison.Ordinal))
            diagnostics.Add(Error("不是有效的 HyPlayer 歌词特效文件。"));
        if (document.SchemaVersion > LyricEffectProfileDocument.CurrentSchemaVersion)
            diagnostics.Add(Error("该配置需要更高版本的 HyPlayer。"));
        if (document.ExpressionApiVersion > LyricEffectProfileDocument.CurrentExpressionApiVersion)
            diagnostics.Add(Error("该配置使用了更高版本的歌词表达式 API。"));
        if (document.Operations.Count > MaximumOperationCount)
            diagnostics.Add(Error($"歌词特效链最多允许 {MaximumOperationCount} 个节点。"));

        var duplicateIds = document.Operations
            .GroupBy(operation => operation.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicateId in duplicateIds)
            diagnostics.Add(Error("节点 instanceId 不能为空或重复。", duplicateId));

        var compiled = new List<CompiledLyricRenderOperation>();
        foreach (var definition in document.Operations.Take(MaximumOperationCount))
        {
            if (!_factories.TryGetValue(definition.TypeId, out var factory))
            {
                diagnostics.Add(new LyricProfileDiagnostic(
                    LyricProfileDiagnosticSeverity.Warning,
                    $"当前版本未安装节点类型“{definition.TypeId}”，该节点会保留但不参与渲染。",
                    definition.InstanceId));
                continue;
            }

            var result = factory.Compile(definition);
            diagnostics.AddRange(result.Diagnostics);
            if (definition.IsEnabled && result.Operation is not null) compiled.Add(result.Operation);
        }

        if (diagnostics.Any(item => item.Severity == LyricProfileDiagnosticSeverity.Error))
            return new LyricProfileCompileResult { Diagnostics = diagnostics };

        return new LyricProfileCompileResult
        {
            Diagnostics = diagnostics,
            Profile = new CompiledLyricEffectProfile(
                Interlocked.Increment(ref _version),
                LyricEffectPresets.CloneProfile(document),
                compiled)
        };
    }

    private static LyricProfileDiagnostic Error(string message, string? instanceId = null)
    {
        return new LyricProfileDiagnostic(LyricProfileDiagnosticSeverity.Error, message, instanceId);
    }
}