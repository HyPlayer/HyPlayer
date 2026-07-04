#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public sealed class TokenLiftGlyphEffect : ILyricGlyphEffect
{
    public void Apply(LyricGlyphEffectContext context, ref LyricGlyphDrawState state)
    {
        if (context.Cluster.Layer == LyricTextLayer.Transliteration &&
            !context.RenderContext.Effects.TransliterationScanning)
        {
            return;
        }

        state.Origin.Y -= context.RenderContext.Specs.SyllableLift *
                          TokenGlyphProgress.GetLiftProgress(
                              context.Cluster,
                              context.Frame,
                              context.RenderContext.Specs.ShortTokenLiftDurationThresholdMs,
                              context.RenderContext.Specs.CooperativeLiftWindow,
                              context.RenderContext.Specs.CooperativeLiftEasingPower);
    }
}
