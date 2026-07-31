using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.LyricEffects.Drawing;

namespace HyPlayer.LyricRenderer.Pipeline;

public sealed class LyricDrawCommandRegistry
{
    private readonly Dictionary<string, ILyricDrawCommandFactory> _factories =
        [with(StringComparer.OrdinalIgnoreCase)];

    public LyricDrawCommandRegistry()
    {
        foreach (var factory in BuiltInDrawCommandFactories.CreateAll()) Register(factory);
    }

    public IReadOnlyList<LyricDrawCommandSignature> Signatures =>
        (LyricDrawCommandSignature[])
        [.. _factories.Values.Select(factory => factory.Signature).OrderBy(signature => signature.Name)];

    public void Register(ILyricDrawCommandFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(factory.Signature.Name, factory))
            throw new InvalidOperationException($"绘图命令“{factory.Signature.Name}”已注册。");
    }

    internal bool TryGet(string name, out ILyricDrawCommandFactory factory)
    {
        return _factories.TryGetValue(name, out factory!);
    }
}
