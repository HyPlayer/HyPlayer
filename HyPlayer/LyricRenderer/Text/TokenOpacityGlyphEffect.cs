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
        var minOpacity = context.RenderContext.Specs.TokenMinOpacity;
        var maxOpacity = context.RenderContext.Specs.TokenMaxOpacity;
        state.Opacity = Math.Clamp(
            minOpacity + (maxOpacity - minOpacity) * progress,
            minOpacity,
            maxOpacity);
    }
}
