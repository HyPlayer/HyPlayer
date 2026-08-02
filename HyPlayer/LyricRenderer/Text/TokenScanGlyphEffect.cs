#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public sealed class TokenScanGlyphEffect : ILyricGlyphEffect
{
    private readonly TokenLiftGlyphEffect _liftEffect = new();
    private readonly TokenOpacityGlyphEffect _opacityEffect = new();

    public void Apply(LyricGlyphEffectContext context, ref LyricGlyphDrawState state)
    {
        _liftEffect.Apply(context, ref state);
        _opacityEffect.Apply(context, ref state);
    }
}