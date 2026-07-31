#nullable enable

using System.Numerics;
using Windows.UI;
using Microsoft.Graphics.Canvas.Text;

namespace HyPlayer.LyricRenderer.Text;

public struct LyricGlyphDrawState
{
    public LyricTextLayer Layer { get; set; }
    public CanvasFontFace FontFace { get; set; }
    public float FontSize { get; set; }
    public CanvasGlyph[] Glyphs { get; set; }
    public Vector2 Origin { get; set; }
    public bool IsSideways { get; set; }
    public uint BidiLevel { get; set; }
    public CanvasTextMeasuringMode MeasuringMode { get; set; }
    public string LocaleName { get; set; }
    public string TextString { get; set; }
    public int[] ClusterMap { get; set; }
    public uint CharacterIndex { get; set; }
    public CanvasGlyphOrientation GlyphOrientation { get; set; }
    public float Opacity { get; set; }
    public float BlurRadius { get; set; }
    public float Scale { get; set; }
    public Color Color { get; set; }
    public bool SkipDraw { get; set; }

    public static LyricGlyphDrawState FromCluster(LyricGlyphCluster cluster, Color color)
    {
        var state = cluster.BaseState;
        state.Opacity = 1;
        state.BlurRadius = 0;
        state.Scale = 1;
        state.Color = color;
        state.SkipDraw = false;
        return state;
    }
}
