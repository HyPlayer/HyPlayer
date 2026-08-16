using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.LyricRenderer.Effect;

public partial class LyricOpacityEffect : LyricEffect<OpacityEffect>
{
    protected override OpacityEffect Effect { get; } = new();
    public EffectProperty Opacity { get; set; } = new((_, _) => 1f);

    public override ICanvasImage Apply(ICanvasImage source, RenderingLyricLine lyricLine, RenderContext context)
    {
        Effect.Source = source;
        Effect.Opacity = Opacity.GetValue(lyricLine, context);
        return Effect;
    }
}
