using System.Numerics;
using Windows.UI.Xaml;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Effect;

public partial class LyricTransform2DEffect : LyricEffect<Transform2DEffect>
{
    public EffectProperty XScale { get; set; } = new((_, _) => 1f);
    public EffectProperty YScale { get; set; } = new((_, _) => 1f);

    public EffectProperty X { get; set; } = new((_, _) => 0f);
    public EffectProperty Y { get; set; } = new((_, _) => 0f);

    protected override Transform2DEffect Effect { get; } = new();

    public override ICanvasImage Apply(ICanvasImage source, RenderingLyricLine lyricLine, RenderContext context)
    {
        Effect.Source = source;
        var isRight = lyricLine.Typography?.Alignment == TextAlignment.Right;
        var matrix = GetCenterMatrix(
            X.GetValue(lyricLine, context),
            Y.GetValue(lyricLine, context),
            isRight ? lyricLine.RenderingWidth : 0, lyricLine.RenderingHeight / 2,
            XScale.GetValue(lyricLine, context),
            YScale.GetValue(lyricLine, context));
        Effect.TransformMatrix = matrix;
        return Effect;
    }

    public static Matrix3x2 GetCenterMatrix(float x, float y, float xCenter, float yCenter, float xScale, float yScale)
    {
        return Matrix3x2.CreateTranslation(-xCenter, -yCenter)
               * Matrix3x2.CreateScale(xScale, yScale)
               * Matrix3x2.CreateTranslation(x, y)
               * Matrix3x2.CreateTranslation(xCenter, yCenter);
    }
}
