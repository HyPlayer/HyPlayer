#nullable enable

using System.Numerics;
using Windows.UI;
using Microsoft.Graphics.Canvas.Text;

namespace HyPlayer.LyricRenderer.Text;

public struct LyricGlyphDrawState
{
    public LyricTextLayer Layer;
    public CanvasFontFace FontFace;
    public float FontSize;
    public CanvasGlyph[] Glyphs;
    public Vector2 Origin;
    public bool IsSideways;
    public uint BidiLevel;
    public CanvasTextMeasuringMode MeasuringMode;
    public string LocaleName;
    public string TextString;
    public int[] ClusterMap;
    public uint CharacterIndex;
    public CanvasGlyphOrientation GlyphOrientation;
    public float Opacity;
    public float BlurRadius;
    public float Scale;
    public Color Color;
    public bool SkipDraw;

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