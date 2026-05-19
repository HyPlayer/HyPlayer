using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.UI;

namespace HyPlayer.UI.Playback.ExpandedCanvas;

/// <summary>
/// Win2D composable layer that renders an FFT spectrum visualization.
/// </summary>
public sealed class SpectrumLayer : IExpandedCanvasLayer
{
    private readonly ExpandedCanvasState _state;
    private readonly AudioGraphPlayer _player;

    private static readonly Color DarkSpectrumColor = Color.FromArgb(32, 0, 0, 0);
    private static readonly Color LightSpectrumColor = Color.FromArgb(32, 255, 255, 255);

    public SpectrumLayer(ExpandedCanvasState state, AudioGraphPlayer player)
    {
        _state = state;
        _player = player;
    }

    public string LayerName => "Spectrum";
    public int Order => 10;

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
        if (!_state.EnableFft) return;

        var fft = _player.FFTProcessor;
        if (fft is null) return;

        float width = (float)sender.Size.Width;
        float height = (float)sender.Size.Height / 2f;
        float remainHeight = (float)sender.Size.Height - height;
        float barWidth = width / FFTProcessor.DisplayBandCount;
        float scaleFactor = height / 80.0f;

        var color = _state.IsBrightTheme ? DarkSpectrumColor : LightSpectrumColor;

        for (int i = 0; i < FFTProcessor.DisplayBandCount; i++)
        {
            float barHeight = System.Math.Clamp(fft.DisplayData[i] * scaleFactor, 0, height - 1);
            session.FillRectangle(
                i * barWidth,
                remainHeight + height - barHeight,
                barWidth,
                barHeight,
                color);
        }
    }
}
