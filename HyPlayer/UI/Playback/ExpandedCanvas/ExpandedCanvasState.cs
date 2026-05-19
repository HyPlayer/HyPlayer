using System.Collections.Generic;
using System.Numerics;
using HyPlayer.Classes;
using HyPlayer.LyricRenderer;
using Microsoft.Graphics.Canvas.Effects;

namespace HyPlayer.UI.Playback.ExpandedCanvas;

public sealed class ExpandedCanvasState
{
    public BackgroundType BackgroundType { get; set; } = BackgroundType.CoverBlur;
    public bool IsPlaying { get; set; }
    public bool EnableFft { get; set; }
    public bool IsBrightTheme { get; set; }
    public bool IsolationLightWave { get; set; }
    public float RandomValue { get; set; } = -1;
    public float LyricRenderXOffset { get; set; }
    public float LyricRenderYOffset { get; set; }
    public ExpandedWindowMode WindowMode { get; set; } = ExpandedWindowMode.Both;
    public IReadOnlyList<Vector3> AlbumColorVectors { get; set; } = [];
    public LyricRenderView? LyricBox { get; set; }
    public PixelShaderEffect? ShaderEffect { get; set; }
}
