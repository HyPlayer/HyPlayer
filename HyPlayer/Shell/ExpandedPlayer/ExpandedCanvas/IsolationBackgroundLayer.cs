using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Windows.UI;
using ComputeSharp.D2D1.Uwp;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.UI.Effects;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
///     Win2D composable layer that owns the Isolation-mode pixel shader background.
/// </summary>
public sealed class IsolationBackgroundLayer : IExpandedCanvasLayer
{
    private const double ColorBlendSeconds = 1.2;
    private const double DimSeconds = 0.9;
    private const float DimBlurAmount = 12;

    private readonly bool _enableDithering = true;
    private readonly bool _enableLightWave;
    private readonly ExpandedCanvasState _state;
    private readonly Vector3[] _colorsFrom = new Vector3[4];
    private readonly Vector3[] _colorsTo = new Vector3[4];
    private readonly float _random1 = Random.Shared.NextSingle();
    private readonly float _random2 = Random.Shared.NextSingle();
    private readonly float _random3 = Random.Shared.NextSingle();

    private PixelShaderEffect<IsolationEffect>? _effect;
    private Vector2 _canvasSize;
    private float3 _color1, _color2, _color3, _color4;
    private long _colorBlendStart;
    private bool _colorsInitialized;
    private GaussianBlurEffect? _dimBlur;
    private float _dimAmount;
    private float _dimProgress;
    private float _dimTarget;
    private long _dimTimestamp;
    private double _shaderSeconds;

    public IsolationBackgroundLayer(ExpandedCanvasState state, LyricSettings setting)
    {
        _state = state;
        _enableDithering = true;
        _enableLightWave = setting.IsolationLightWave;
    }

    public string LayerName => "IsolationBackground";
    public int Order => 0;
    public bool NeedsFrames =>
        _state.BackgroundType == BackgroundType.Isolation &&
        (_state.IsPlaying || _dimProgress != _dimTarget || Volatile.Read(ref _colorBlendStart) != 0);

    /// <inheritdoc />
    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        _effect = new PixelShaderEffect<IsolationEffect>();
        _canvasSize = new Vector2(
            sender.ConvertDipsToPixels((float)sender.ActualWidth, CanvasDpiRounding.Round),
            sender.ConvertDipsToPixels((float)sender.ActualHeight, CanvasDpiRounding.Round));
    }

    /// <inheritdoc />
    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        if (_dimProgress != _dimTarget)
        {
            var now = Stopwatch.GetTimestamp();
            var step = (float)(Stopwatch.GetElapsedTime(Volatile.Read(ref _dimTimestamp), now).TotalSeconds /
                               DimSeconds);
            Volatile.Write(ref _dimTimestamp, now);
            _dimProgress = _dimTarget > _dimProgress
                ? Math.Min(_dimTarget, _dimProgress + step)
                : Math.Max(_dimTarget, _dimProgress - step);
        }

        if (_colorsInitialized && Volatile.Read(ref _colorBlendStart) != 0)
        {
            var blend = ColorBlendProgress();
            ApplyColorBlend(blend);
            if (blend >= 1) Volatile.Write(ref _colorBlendStart, 0);
        }

        if (_state.IsPlaying) _shaderSeconds += args.Timing.ElapsedTime.TotalSeconds;

        _canvasSize = PixelSize(sender);
        ApplyConstants();
    }

    private static Vector2 PixelSize(ICanvasAnimatedControl sender)
    {
        var size = sender.Size;
        return new Vector2(
            sender.ConvertDipsToPixels((float)size.Width, CanvasDpiRounding.Round),
            sender.ConvertDipsToPixels((float)size.Height, CanvasDpiRounding.Round));
    }

    private void ApplyConstants()
    {
        _effect.ConstantBuffer = new IsolationEffect(
            _canvasSize,
            (float)_shaderSeconds,
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
        var pixelSize = PixelSize(sender);
        if (pixelSize != _canvasSize)
        {
            _canvasSize = pixelSize;
            ApplyConstants();
        }

        var progress = _dimProgress;
        if (progress <= 0.001f)
        {
            session.DrawImage(_effect);
            return;
        }

        var eased = progress * progress * (3 - 2 * progress);
        _dimBlur ??= new GaussianBlurEffect { BorderMode = EffectBorderMode.Hard };
        _dimBlur.Source = _effect;
        _dimBlur.BlurAmount = DimBlurAmount * eased;
        session.DrawImage(_dimBlur);
        var size = sender.Size;
        session.FillRectangle(0, 0, (float)size.Width, (float)size.Height,
            Color.FromArgb((byte)(255 * _dimAmount * eased), 0, 0, 0));
    }

    public void DisposeShader()
    {
        _dimBlur?.Dispose();
        _dimBlur = null;
        _effect?.Dispose();
    }

    public void SetBrightDim(bool active, float amount)
    {
        if (active) _dimAmount = amount;
        _dimTarget = active ? 1 : 0;
        Volatile.Write(ref _dimTimestamp, Stopwatch.GetTimestamp());
    }

    public void ApplyShaderProperties()
    {
        var colors = _state.AlbumColorVectors;
        if (colors.Count < 4) return;
        if (!_colorsInitialized)
        {
            for (var i = 0; i < 4; i++) _colorsFrom[i] = _colorsTo[i] = colors[i];
            _colorsInitialized = true;
            ApplyColorBlend(1);
            return;
        }

        var ordered = MatchPaletteOrder(colors, _colorsTo);
        var difference = 0f;
        for (var i = 0; i < 4; i++) difference += Vector3.Distance(ordered[i], _colorsTo[i]);
        if (difference < 0.03f) return;
        var progress = ColorBlendProgress();
        for (var i = 0; i < 4; i++)
        {
            _colorsFrom[i] = Vector3.Lerp(_colorsFrom[i], _colorsTo[i], progress);
            _colorsTo[i] = ordered[i];
        }

        Volatile.Write(ref _colorBlendStart, Stopwatch.GetTimestamp());
    }

    private static Vector3[] MatchPaletteOrder(IReadOnlyList<Vector3> colors, Vector3[] slots)
    {
        var result = new Vector3[4];
        var used = new bool[4];
        var assigned = new bool[4];
        for (var round = 0; round < 4; round++)
        {
            var best = float.MaxValue;
            var bestSlot = 0;
            var bestColor = 0;
            for (var slot = 0; slot < 4; slot++)
            {
                if (assigned[slot]) continue;
                for (var color = 0; color < 4; color++)
                {
                    if (used[color]) continue;
                    var distance = Vector3.DistanceSquared(colors[color], slots[slot]);
                    if (distance >= best) continue;
                    best = distance;
                    bestSlot = slot;
                    bestColor = color;
                }
            }

            result[bestSlot] = colors[bestColor];
            assigned[bestSlot] = true;
            used[bestColor] = true;
        }

        return result;
    }

    private float ColorBlendProgress()
    {
        var start = Volatile.Read(ref _colorBlendStart);
        if (start == 0) return 1;
        var t = Math.Clamp(Stopwatch.GetElapsedTime(start).TotalSeconds / ColorBlendSeconds, 0, 1);
        return (float)(t * t * (3 - 2 * t));
    }

    private void ApplyColorBlend(float t)
    {
        _color1 = Vector3.Lerp(_colorsFrom[0], _colorsTo[0], t);
        _color2 = Vector3.Lerp(_colorsFrom[1], _colorsTo[1], t);
        _color3 = Vector3.Lerp(_colorsFrom[2], _colorsTo[2], t);
        _color4 = Vector3.Lerp(_colorsFrom[3], _colorsTo[3], t);
    }
}