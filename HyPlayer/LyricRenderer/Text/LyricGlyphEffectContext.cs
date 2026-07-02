#nullable enable

using HyPlayer.LyricRenderer.Abstraction;

namespace HyPlayer.LyricRenderer.Text;

public readonly ref struct LyricGlyphEffectContext
{
    public LyricGlyphEffectContext(
        RenderContext renderContext,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        LyricGlyphCluster cluster)
    {
        RenderContext = renderContext;
        Layout = layout;
        Frame = frame;
        Cluster = cluster;
    }

    public RenderContext RenderContext { get; }
    public LyricTextLayoutSnapshot Layout { get; }
    public TextRenderFrame Frame { get; }
    public LyricGlyphCluster Cluster { get; }
}
