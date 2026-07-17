using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;

namespace HyPlayer.LyricRenderer.Effect;

public delegate float EffectExpression(RenderingLyricLine lyricLine, RenderContext context);