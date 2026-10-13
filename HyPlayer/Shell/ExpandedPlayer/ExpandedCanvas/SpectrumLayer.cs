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
    private readonly float[] _displayData = new float[FFTProcessor.DisplayBandCount];
    private float _visibility;

    public SpectrumLayer(ExpandedCanvasState state, AudioGraphPlayer player)
    {
        _state = state;
        _player = player;
    }

    public string LayerName => "Spectrum";
    public int Order => 10;
    public bool NeedsFrames => _state.ShowSpectrum && _visibility > 0f;

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
        if (!_state.ShowSpectrum) return;

        var fft = _player.FFTProcessor;
        var hasData = fft is not null && fft.TryCopyDisplayData(_displayData);
        var target = hasData && _player.GlobalPlaybackStatus == PlaybackStatus.Playing ? 1f : 0f;
        var step = Math.Clamp((float)timing.ElapsedTime.TotalSeconds * 8f, 0f, 1f);
        _visibility += (target - _visibility) * step;
        if (_visibility < 0.004f)
        {
            _visibility = 0f;
            return;
        }

        var size = sender.Size;
        var width = (float)size.Width;
        var height = (float)size.Height / 2f;
        var remainHeight = (float)size.Height - height;
        var barWidth = width / FFTProcessor.DisplayBandCount;
        var scaleFactor = height / 80.0f;

        var color = _state.IsBrightTheme ? _darkSpectrumColor : _lightSpectrumColor;

        for (var i = 0; i < FFTProcessor.DisplayBandCount; i++)
        {
            var barHeight = Math.Clamp(_displayData[i] * scaleFactor, 0, height - 1) * _visibility;
            session.FillRectangle(
                i * barWidth,
                remainHeight + height - barHeight,
                barWidth,
                barHeight,
                color);
        }
    }
}
