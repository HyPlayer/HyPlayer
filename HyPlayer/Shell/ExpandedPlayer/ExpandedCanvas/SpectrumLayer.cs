using System;
using Windows.UI;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
///     Win2D composable layer that renders an FFT spectrum visualization.
/// </summary>
public sealed class SpectrumLayer : IExpandedCanvasLayer
{
    private static readonly Color _darkSpectrumColor = Color.FromArgb(32, 0, 0, 0);
    private static readonly Color _lightSpectrumColor = Color.FromArgb(32, 255, 255, 255);
    private readonly AudioGraphPlayer _player;
    private readonly ExpandedCanvasState _state;

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

        var width = (float)sender.Size.Width;
        var height = (float)sender.Size.Height / 2f;
        var remainHeight = (float)sender.Size.Height - height;
        var barWidth = width / FFTProcessor.DisplayBandCount;
        var scaleFactor = height / 80.0f;

        var color = _state.IsBrightTheme ? _darkSpectrumColor : _lightSpectrumColor;

        for (var i = 0; i < FFTProcessor.DisplayBandCount; i++)
        {
            var barHeight = Math.Clamp(fft.DisplayData[i] * scaleFactor, 0, height - 1);
            session.FillRectangle(
                i * barWidth,
                remainHeight + height - barHeight,
                barWidth,
                barHeight,
                color);
        }
    }
}