using CommunityToolkit.WinUI.Media;
using HyPlayer.Classes;
using Impressionist.Implementations;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
/// Win2D composable layer that owns the Isolation-mode pixel shader background.
/// </summary>
public sealed class BackgroundShaderLayer : IExpandedCanvasLayer
{
    private readonly ExpandedCanvasState _state;

    public BackgroundShaderLayer(ExpandedCanvasState state)
    {
        _state = state;
    }

    public string LayerName => "BackgroundShader";
    public int Order => 0;

    /// <inheritdoc />
    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        var resolution = new Vector2(
             sender.ConvertDipsToPixels((float)sender.ActualWidth, CanvasDpiRounding.Round),
             sender.ConvertDipsToPixels((float)sender.ActualHeight, CanvasDpiRounding.Round));
        if (_state.BackgroundType == BackgroundType.Isolation && _state.ShaderEffect is null)
        {
            args.TrackAsyncAction(LoadShaderAsync(resolution).AsAsyncAction());
        }
        ApplyShaderProperties();
    }

    /// <inheritdoc />
    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        if (_state.BackgroundType != BackgroundType.Isolation) return;
        if (!_state.IsPlaying) return;

        var shader = _state.ShaderEffect;
        if (shader is null) return;

        shader.Properties["iTime"] = (float)args.Timing.TotalTime.TotalSeconds + _state.RandomValue;
    }

    /// <inheritdoc />
    public void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing)
    {
        if (_state.BackgroundType != BackgroundType.Isolation) return;

        var shader = _state.ShaderEffect;
        if (shader is not null)
            session.DrawImage(shader);
    }

    public void DisposeShader()
    {
        _state.ShaderEffect?.Dispose();
        _state.ShaderEffect = null;
    }

    public void UpdateResolution(float width, float height)
    {
        _state.ShaderEffect?.Properties["iResolution"] = new Vector2(width, height);
    }

    public void ApplyShaderProperties()
    {
        var shader = _state.ShaderEffect;
        if (shader is null) return;

        var colors = _state.AlbumColorVectors;
        if (colors is { Count: >= 4 })
        {
            shader.Properties["color1"] = colors[0];
            shader.Properties["color2"] = colors[1];
            shader.Properties["color3"] = colors[2];
            shader.Properties["color4"] = colors[3];
            shader.Properties["UseHSVBlending"] = UseHSVBlending(colors);
            shader.Properties["EnableLightWave"] = _state.IsolationLightWave;
            shader.Properties["RandomValue1"] = (float)Random.Shared.Next(-50, +50);
            shader.Properties["RandomValue2"] = (float)Random.Shared.Next(-50, +50);
            shader.Properties["RandomValue3"] = (float)Random.Shared.Next(-50, +50);
        }
    }

    private async Task LoadShaderAsync(Vector2 resolution)
    {
        StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Shaders/BackgroundShader.bin"));
        IBuffer buffer = await FileIO.ReadBufferAsync(file);
        _state.ShaderEffect = new PixelShaderEffect(buffer.ToArray());
        _state.RandomValue = Random.Shared.Next(100);
        _state.ShaderEffect?.Properties["iResolution"] = resolution;
    }

    private static bool UseHSVBlending(IReadOnlyList<Vector3> colorVectors)
    {
        if (colorVectors.Count == 0)
            return false;

        var x = 0d;
        var y = 0d;
        var weightSum = 0d;

        foreach (var colorVector in colorVectors)
        {
            var hsv = colorVector.RGBVectorToHSVColor();
            var weight = hsv.S * hsv.V;

            if (weight <= 0.05d)
                continue;

            var radians = hsv.H * Math.PI / 180d;
            x += Math.Cos(radians) * weight;
            y += Math.Sin(radians) * weight;
            weightSum += weight;
        }

        if (weightSum <= 0d)
            return false;

        var hueConcentration = Math.Sqrt(x * x + y * y) / weightSum;
        return hueConcentration >= 0.7d;
    }
}
