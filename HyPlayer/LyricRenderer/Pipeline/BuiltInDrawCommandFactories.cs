using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.UI;
using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Models;
using Microsoft.Graphics.Canvas.Text;

namespace HyPlayer.LyricRenderer.Pipeline;

internal static class BuiltInDrawCommandFactories
{
    private static readonly LyricExpressionValueType _s = LyricExpressionValueType.Scalar;
    private static readonly LyricExpressionValueType _c = LyricExpressionValueType.Color;
    private static readonly LyricExpressionValueType _t = LyricExpressionValueType.Text;

    public static IReadOnlyList<ILyricDrawCommandFactory> CreateAll()
    {
        return (ILyricDrawCommandFactory[])
        [
            Command("FillRectangle", (LyricExpressionValueType[])[_s, _s, _s, _s, _c], (context, value) =>
                context.Session.FillRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]))),
            Command("StrokeRectangle", (LyricExpressionValueType[])[_s, _s, _s, _s, _c, _s], (context, value) =>
                context.Session.DrawRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]), value[5].Scalar)),
            Command("FillRoundedRectangle", (LyricExpressionValueType[])[_s, _s, _s, _s, _s, _c], (context, value) =>
                context.Session.FillRoundedRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    value[4].Scalar, value[4].Scalar, Color(value[5]))),
            Command("StrokeRoundedRectangle", (LyricExpressionValueType[])[_s, _s, _s, _s, _s, _c, _s], (context, value) =>
                context.Session.DrawRoundedRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    value[4].Scalar, value[4].Scalar, Color(value[5]), value[6].Scalar)),
            Command("FillEllipse", (LyricExpressionValueType[])[_s, _s, _s, _s, _c], (context, value) =>
                context.Session.FillEllipse(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]))),
            Command("StrokeEllipse", (LyricExpressionValueType[])[_s, _s, _s, _s, _c, _s], (context, value) =>
                context.Session.DrawEllipse(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]), value[5].Scalar)),
            Command("DrawLine", (LyricExpressionValueType[])[_s, _s, _s, _s, _c, _s], (context, value) =>
                context.Session.DrawLine(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]), value[5].Scalar)),
            Command("DrawText", (LyricExpressionValueType[])[_t, _s, _s, _s, _c], DrawText),
            Command("Save", [], (context, _) => context.Save()),
            Command("Restore", [], (context, _) => context.Restore()),
            Command("Translate", (LyricExpressionValueType[])[_s, _s], (context, value) =>
                context.Session.Transform *= Matrix3x2.CreateTranslation(value[0].Scalar, value[1].Scalar)),
            Command("Scale", (LyricExpressionValueType[])[_s, _s, _s, _s], (context, value) =>
                context.Session.Transform *= Matrix3x2.CreateScale(value[0].Scalar, value[1].Scalar,
                    new Vector2(value[2].Scalar, value[3].Scalar))),
            Command("Rotate", (LyricExpressionValueType[])[_s, _s, _s], (context, value) =>
                context.Session.Transform *= Matrix3x2.CreateRotation(MathF.PI * value[0].Scalar / 180f,
                    new Vector2(value[1].Scalar, value[2].Scalar)))
        ];
    }

    private static DelegateDrawCommandFactory Command(
        string name,
        IReadOnlyList<LyricExpressionValueType> arguments,
        Action<LyricDrawExecutionContext, IReadOnlyList<LyricDrawValue>> execute)
    {
        return new DelegateDrawCommandFactory(new LyricDrawCommandSignature(name, arguments), execute);
    }

    private static void DrawText(LyricDrawExecutionContext context, IReadOnlyList<LyricDrawValue> values)
    {
        using var format = new CanvasTextFormat { FontSize = Math.Max(values[3].Scalar, 1) };
        context.Session.DrawText(values[0].Text ?? string.Empty, new Vector2(values[1].Scalar, values[2].Scalar),
            Color(values[4]), format);
    }

    private static Color Color(LyricDrawValue value)
    {
        return Windows.UI.Color.FromArgb(value.Color.A, value.Color.R, value.Color.G, value.Color.B);
    }

    private sealed class DelegateDrawCommandFactory(
        LyricDrawCommandSignature signature,
        Action<LyricDrawExecutionContext, IReadOnlyList<LyricDrawValue>> execute) : ILyricDrawCommandFactory
    {
        public LyricDrawCommandSignature Signature { get; } = signature;

        public void Execute(LyricDrawExecutionContext context, IReadOnlyList<LyricDrawValue> arguments)
        {
            execute(context, arguments);
        }
    }
}
