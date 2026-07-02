#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public interface ILyricGlyphEffect
{
    void Apply(LyricGlyphEffectContext context, ref LyricGlyphDrawState state);
}
