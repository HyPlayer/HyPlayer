using System;
using System.Numerics;
using ComputeSharp.D2D1.Uwp;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.UI.Effects;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
///     Win2D composable layer that owns the Isolation-mode pixel shader background.
/// </summary>
public sealed class BackgroundShaderLayer : IExpandedCanvasLayer
{
    private readonly bool _enableDithering = true;
    private readonly bool _enableLightWave;
    private readonly ExpandedCanvasState _state;

    private Vector2 _canvasSize;
    private float3 _color1, _color2, _color3, _color4;
    private float _random1, _random2, _random3;

    public BackgroundShaderLayer(ExpandedCanvasState state, LyricSettings setting)
    {
        _state = state;
        _state.IsolationEffect = new PixelShaderEffect<IsolationEffect>();
        _enableDithering = true;
        _enableLightWave = setting.IsolationLightWave;
    }

    public string LayerName => "BackgroundShader";
    public int Order => 0;

    /// <inheritdoc />
    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        _canvasSize = new Vector2(
            sender.ConvertDipsToPixels((float)sender.ActualWidth, CanvasDpiRounding.Round),
            sender.ConvertDipsToPixels((float)sender.ActualHeight, CanvasDpiRounding.Round));
    }

    /// <inheritdoc />
    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        _state.IsolationEffect?.ConstantBuffer = new IsolationEffect(
            _canvasSize,
            (float)args.Timing.TotalTime.TotalSeconds,
            _color1,
            _color2,
            _color3,
            _color4,
            _random1,
            _random2,
            _random3,
            _enableLightWave,
            _enableDithering);
    }

    /// <inheritdoc />
    public void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing)
    {
        if (_state.BackgroundType != BackgroundType.Isolation) return;

        var shader = _state.IsolationEffect;
        if (shader is not null)
            session.DrawImage(shader);
    }

    public void DisposeShader()
    {
        _state.IsolationEffect?.Dispose();
        _state.IsolationEffect = null;
    }

    public void UpdateResolution(float width, float height)
    {
        _canvasSize = new Vector2(width, height);
    }

    public void ApplyShaderProperties()
    {
        var shader = _state.IsolationEffect;
        if (shader is null) return;

        var colors = _state.AlbumColorVectors;
        _color1 = colors[0];
        _color2 = colors[1];
        _color3 = colors[2];
        _color4 = colors[3];
        _random1 = Random.Shared.NextSingle();
        _random2 = Random.Shared.NextSingle();
        _random3 = Random.Shared.NextSingle();
    }
}