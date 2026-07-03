#nullable enable

using System;

namespace HyPlayer.LyricRenderer.Text;

public sealed class TokenOpacityGlyphEffect : ILyricGlyphEffect
{
    public void Apply(LyricGlyphEffectContext context, ref LyricGlyphDrawState state)
    {
        if (context.Cluster.Layer == LyricTextLayer.Transliteration &&
            !context.RenderContext.Effects.TransliterationScanning)
        {
            state.Opacity = 1;
            return;
        }

        var progress = TokenGlyphProgress.GetHighlightProgress(context.Cluster, context.Frame);
        state.Opacity = Math.Clamp(0.3f + 0.7f * progress, 0.3f, 1f);
    }
}
