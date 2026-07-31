using System;
using System.Collections.Generic;
using System.Numerics;
using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Pipeline;

public readonly record struct LyricDrawValue(
    LyricExpressionValueType Type,
    float Scalar,
    LyricColorValue Color,
    string? Text)
{
    public static LyricDrawValue FromScalar(float value)
    {
        return new LyricDrawValue(LyricExpressionValueType.Scalar, value, default, null);
    }

    public static LyricDrawValue FromColor(LyricColorValue value)
    {
        return new LyricDrawValue(LyricExpressionValueType.Color, 0, value, null);
    }

    public static LyricDrawValue FromText(string value)
    {
        return new LyricDrawValue(LyricExpressionValueType.Text, 0, default, value);
    }
}

public interface ILyricDrawCommandFactory
{
    LyricDrawCommandSignature Signature { get; }

    void Execute(LyricDrawExecutionContext context, IReadOnlyList<LyricDrawValue> arguments);
}

public sealed class LyricDrawExecutionContext
{
    private readonly Stack<Matrix3x2> _transforms = new();

    internal LyricDrawExecutionContext(CanvasDrawingSession session)
    {
        Session = session;
    }

    public CanvasDrawingSession Session { get; }

    public void Save()
    {
        _transforms.Push(Session.Transform);
    }

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
