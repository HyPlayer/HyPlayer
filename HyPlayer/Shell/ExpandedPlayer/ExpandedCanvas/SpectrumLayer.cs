using System;
using System.Numerics;
using Windows.UI;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
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
    private readonly Vector2[] _points = new Vector2[FFTProcessor.DisplayBandCount];
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

        var size = _state.Viewport(sender.Size);
        var width = (float)size.Width;
        var height = (float)size.Height / 2f;
        var remainHeight = (float)size.Height - height;
        var bandSpacing = width / (FFTProcessor.DisplayBandCount - 1);
        var scaleFactor = height / 80.0f;

        var color = _state.IsBrightTheme ? _darkSpectrumColor : _lightSpectrumColor;

        for (var i = 0; i < FFTProcessor.DisplayBandCount; i++)
        {
            var barHeight = Math.Clamp(_displayData[i] * scaleFactor, 0, height - 1) * _visibility;
            _points[i] = new Vector2(i * bandSpacing, remainHeight + height - barHeight);
        }

        using var geometry = CreateGeometry(session, _points, (float)size.Height);
        session.FillGeometry(geometry, color);
    }

    internal static CanvasGeometry CreateGeometry(ICanvasResourceCreator creator, ReadOnlySpan<Vector2> points,
        float bottom)
    {
        using var path = new CanvasPathBuilder(creator);
        path.BeginFigure(new Vector2(points[0].X, bottom));
        path.AddLine(points[0]);
        for (var i = 0; i < points.Length - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            var step = (end.X - start.X) / 3f;
            path.AddCubicBezier(new Vector2(start.X + step, start.Y + Tangent(points, i) / 3f),
                new Vector2(end.X - step, end.Y - Tangent(points, i + 1) / 3f), end);
        }

        path.AddLine(new Vector2(points[^1].X, bottom));
        path.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(path);
    }

    private static float Tangent(ReadOnlySpan<Vector2> points, int index)
    {
        if (index == 0 || index == points.Length - 1) return 0;
        var before = points[index].Y - points[index - 1].Y;
        var after = points[index + 1].Y - points[index].Y;
        // Monotone tangents keep the curve inside the neighboring peaks and the baseline.
        return before * after <= 0 ? 0 : 2f * before * after / (before + after);
    }
}
