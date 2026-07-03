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
    public required float AdvanceWidth { get; init; }
    public required float VisualLeft { get; init; }
    public required float VisualTop { get; init; }
    public required float VisualRight { get; init; }
    public required float VisualBottom { get; init; }
    public float VisualWidth { get; set; }
    public float VisualHeight { get; set; }

    public int LayerClusterIndex { get; set; }
    public int LayerClusterCount { get; set; }
    public int TokenClusterIndex { get; set; }
    public int TokenClusterCount { get; set; }
    public int VisualLineIndex { get; set; }
    public int LayerVisualLineCount { get; set; }
    public int VisualLineClusterIndex { get; set; }
    public int VisualLineClusterCount { get; set; }
    public int VisualLineStartClusterIndex { get; set; }
    public int VisualLineEndClusterIndexExclusive { get; set; }
}
