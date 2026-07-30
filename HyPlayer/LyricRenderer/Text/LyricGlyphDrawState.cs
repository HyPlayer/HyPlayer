#nullable enable

using Microsoft.Graphics.Canvas.Text;
using System.Numerics;
using Windows.UI;

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
    public float ScaleX;
    public float ScaleY;
    public float Rotation;
    public float RotationX;
    public float RotationY;
    public float PerspectiveDepth;
    public Color Color;
    public float GlowRadius;
    public float GlowOpacity;
    public Color GlowColor;
    public float StrokeWidth;
    public Color StrokeColor;
    public Vector2 ShadowOffset;
    public float ShadowBlur;
    public float ShadowOpacity;
    public Color ShadowColor;
    public bool SkipDraw;

    public static LyricGlyphDrawState FromCluster(LyricGlyphCluster cluster, Color color)
    {
        var state = cluster.BaseState;
        state.Opacity = 1;
        state.BlurRadius = 0;
        state.Scale = 1;
        state.ScaleX = 1;
        state.ScaleY = 1;
        state.Rotation = 0;
        state.RotationX = 0;
        state.RotationY = 0;
        state.PerspectiveDepth = 3000;
        state.Color = color;
        state.SkipDraw = false;
        state.GlowRadius = 0;
        state.GlowOpacity = 0;
        state.StrokeWidth = 0;
        state.ShadowOffset = Vector2.Zero;
        state.ShadowBlur = 0;
        state.ShadowOpacity = 0;
        return state;
    }
}
