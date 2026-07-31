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
    private float3 color1, color2, color3, color4;
    private float random1, random2, random3;

    public BackgroundShaderLayer(ExpandedCanvasState state, Setting setting)
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
            color1,
            color2,
            color3,
            color4,
            random1,
            random2,
            random3,
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
        color1 = colors[0];
        color2 = colors[1];
        color3 = colors[2];
        color4 = colors[3];
        random1 = Random.Shared.NextSingle();
        random2 = Random.Shared.NextSingle();
        random3 = Random.Shared.NextSingle();
    }
}