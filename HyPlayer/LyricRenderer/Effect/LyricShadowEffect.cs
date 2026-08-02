using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Effect;

public class LyricShadowEffect : LyricEffect<ShadowEffect>
{
    protected override ShadowEffect Effect { get; } = new();

    public EffectProperty BlurAmount { get; set; } = new((_, _) => 0);

    public override ICanvasImage Apply(ICanvasImage source, RenderingLyricLine lyricLine, RenderContext context)
    {
        Effect.Source = source;
        Effect.BlurAmount = BlurAmount.GetValue(lyricLine, context);
        return Effect;
    }
}