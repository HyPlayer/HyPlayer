using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using ComputeSharp.D2D1.Uwp;
using HyPlayer.Domain;
using HyPlayer.LyricRenderer;
using HyPlayer.UI.Effects;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

public sealed class ExpandedCanvasState
{
    public BackgroundType BackgroundType { get; set; } = BackgroundType.CoverBlur;
    public bool IsPlaying { get; set; }
    public bool IsTransitioning { get; set; }
    public long TrackChangeTimestamp { get; set; }
    public bool ShowSpectrum { get; set; }
    public bool ShowDebugOverlay { get; set; }
    public bool IsCanvasIdle { get; set; }
    public CanvasFramePacer FramePacer { get; } = new();
    public bool IsBrightTheme { get; set; }
    public double LastFrameDrawMilliseconds { get; set; }
    public float RasterizationScale { get; set; } = 1;
    public float RandomValue { get; set; } = -1;
    public float LyricRenderXOffset { get; set; }
    public float LyricRenderYOffset { get; set; }
    public float LyricOpacity { get; set; } = 1;
    public ExpandedWindowMode WindowMode { get; set; } = ExpandedWindowMode.Both;
    public IReadOnlyList<Vector3> AlbumColorVectors { get; set; } = [];
    public LyricRenderView? LyricBox { get; set; }
    public float Overscan { get; set; }

    public Size Viewport(Size canvas)
    {
        var overscan = Overscan;
        return new Size(Math.Max(0, canvas.Width - overscan), Math.Max(0, canvas.Height - overscan));
    }
}
