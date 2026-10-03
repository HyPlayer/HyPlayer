using HyPlayer.Domain;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System.Numerics;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
///     Win2D composable layer that renders lyrics through the shared LyricRenderView.
/// </summary>
public sealed class LyricsLayer : IExpandedCanvasLayer
{
    private readonly ExpandedCanvasState _state;

    public LyricsLayer(ExpandedCanvasState state)
    {
        _state = state;
    }

    public string LayerName => "Lyrics";
    public int Order => 20;

    /// <inheritdoc />
    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
    }

    /// <inheritdoc />
    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
    }

    /// <inheritdoc />
    public void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing)
    {
        if (_state.WindowMode == ExpandedWindowMode.CoverOnly) return;

        var box = _state.LyricBox;
        if (box is null) return;

        var originalTransform = session.Transform;
        try
        {
            session.Transform = Matrix3x2.CreateTranslation(
                _state.LyricRenderXOffset,
                _state.LyricRenderYOffset) * originalTransform;
            box.Draw(session, timing);
        }
        finally
        {
            session.Transform = originalTransform;
        }
    }
}
