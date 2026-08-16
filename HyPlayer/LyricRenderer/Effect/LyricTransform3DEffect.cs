using System;
using System.Numerics;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Effect;

public partial class LyricTransform3DEffect : LyricEffect<Transform3DEffect>
{
    public EffectProperty Depth { get; set; } = new((_, _) => 3000f);
    public EffectProperty AngleX { get; set; } = new((_, _) => 0f);
    public EffectProperty AngleY { get; set; } = new((_, _) => 0f);
    public EffectProperty AngleZ { get; set; } = new((_, _) => 0f);

    protected override Transform3DEffect Effect { get; } = new();

    public override ICanvasImage Apply(ICanvasImage source, RenderingLyricLine lyricLine, RenderContext context)
    {
        Effect.Source = source;
        var matrix = Get3DMatrix(
            new Vector3(0, lyricLine.RenderingHeight / 2, 0),
            AngleX.GetValue(lyricLine, context),
            AngleY.GetValue(lyricLine, context),
            AngleZ.GetValue(lyricLine, context),
            Depth.GetValue(lyricLine, context));
        Effect.TransformMatrix = matrix;
        return Effect;
    }

    private Matrix4x4 Get3DMatrix(Vector3 center, float angleX = 0,
        float angleY = 0, float angleZ = 0, float depth = 800f)
    {
        var parallaxTranslation = Matrix4x4.Identity;


        var rotationX = (float)(Math.PI * angleX / 180.0);
        var rotationY = (float)(Math.PI * angleY / 180.0);
        var rotationZ = (float)(Math.PI * angleZ / 180.0);

        var rotation = Matrix4x4.CreateRotationX(rotationX) *
                       Matrix4x4.CreateRotationY(rotationY) *
                       Matrix4x4.CreateRotationZ(rotationZ);

        var perspective = Matrix4x4.Identity;
        if (depth > 0) perspective.M34 = 1.0f / depth;

        return Matrix4x4.CreateTranslation(-center) * rotation * perspective *
               Matrix4x4.CreateTranslation(center) * parallaxTranslation;
    }
}
