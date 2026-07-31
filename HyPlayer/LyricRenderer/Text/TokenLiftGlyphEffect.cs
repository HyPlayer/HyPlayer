#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public sealed class TokenLiftGlyphEffect : ILyricGlyphEffect
{
    private const float LiftAmount = 3;

    public void Apply(LyricGlyphEffectContext context, ref LyricGlyphDrawState state)
    {
        if (context.Cluster.Layer == LyricTextLayer.Transliteration &&
            !context.RenderContext.Effects.TransliterationScanning)
            return;

        state.Origin.Y -= LiftAmount * TokenGlyphProgress.GetLiftProgress(context.Cluster, context.Frame);
    }
}