using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;

namespace HyPlayer.LyricRenderer.Effect;

public partial class LyricBlurEffect : LyricEffect<GaussianBlurEffect>
{
    protected override GaussianBlurEffect Effect { get; } = new GaussianBlurEffect();

    public EffectProperty Amount { get; set; } = new((lyricLine, context) =>
    {
        var gap = lyricLine.IsActive ? 0 : Math.Clamp(Math.Abs(lyricLine.RuntimeIndex - context.CurrentLyricLineIndex), 1, 250);
        var blur = (context.IsScrolling) ? 0 : Math.Clamp(gap, 0, 250);
        return blur;
    });

    public override ICanvasImage Apply(ICanvasImage source, RenderingLyricLine lyricLine, RenderContext context)
    {
        Effect.Source = source;
        Effect.BlurAmount = Amount.GetValue(lyricLine, context);
        return Effect;
    }
}
