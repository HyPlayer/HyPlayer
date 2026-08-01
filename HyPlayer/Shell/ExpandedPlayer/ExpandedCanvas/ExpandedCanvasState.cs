using System.Collections.Generic;
using System.Numerics;
using ComputeSharp.D2D1.Uwp;
using HyPlayer.Domain;
using HyPlayer.LyricRenderer;
using HyPlayer.UI.Effects;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

public sealed class ExpandedCanvasState
{
    public BackgroundType BackgroundType { get; set; } = BackgroundType.CoverBlur;
    public bool IsPlaying { get; set; }
    public bool ShowSpectrum { get; set; }
    public bool IsBrightTheme { get; set; }
    public float RandomValue { get; set; } = -1;
    public float LyricRenderXOffset { get; set; }
    public float LyricRenderYOffset { get; set; }
    public ExpandedWindowMode WindowMode { get; set; } = ExpandedWindowMode.Both;
    public IReadOnlyList<Vector3> AlbumColorVectors { get; set; } = [];
    public LyricRenderView? LyricBox { get; set; }
    public PixelShaderEffect<IsolationEffect>? IsolationEffect { get; set; }
}
