#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public sealed class LyricGlyphCluster
{
    public required LyricTextLayer Layer { get; init; }
    public required LyricGlyphDrawState BaseState { get; init; }
    public required int SourceStart { get; init; }
    public required int SourceEnd { get; init; }
    public required int TokenStartIndex { get; init; }
    public required int TokenEndIndexExclusive { get; init; }

    public int LayerClusterIndex { get; set; }
    public int LayerClusterCount { get; set; }
    public int TokenClusterIndex { get; set; }
    public int TokenClusterCount { get; set; }
}
