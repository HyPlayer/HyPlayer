using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;
using HyPlayer.Domain;
using HyPlayer.UI.Effects.LikeApple;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
/// Vortice/D3D11 implementation of the Apple Music-inspired expanded-player backdrop.
/// </summary>
public sealed partial class LikeAppleBackgroundLayer : IExpandedCanvasLayer, IDisposable
{
    private readonly AudioGraphPlayer _player;
    private readonly ExpandedCanvasState _state;
    private CanvasDevice? _device;
    private CanvasRenderTarget? _frame;
    private LikeAppleBackgroundRenderer? _renderer;
    private bool _isVisible;
    private int _artworkRequestVersion;

    public LikeAppleBackgroundLayer(ExpandedCanvasState state, AudioGraphPlayer player)
    {
        _state = state;
        _player = player;
    }

    public string LayerName => "LikeAppleBackground";
    public int Order => 0;

    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        DisposeRenderer();
        _device = sender.Device;
        _renderer = new LikeAppleBackgroundRenderer(
            sender.Device,
            _player.FFTProcessor,
            lightTheme: _state.IsBrightTheme);
        _renderer.SetIsBehindLyrics(true, animate: false);
        _isVisible = false;
    }

    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        var renderer = _renderer;
        bool shouldRender = _state.BackgroundType == BackgroundType.LikeApple;
        if (renderer is null || !shouldRender)
        {
            if (_isVisible) renderer?.SetPresentationVisible(false);
            _isVisible = false;
            _frame = null;
            return;
        }

        if (!_isVisible)
        {
            renderer.SetPresentationVisible(true);
            _isVisible = true;
        }

        if (sender.Size.Width <= 0 || sender.Size.Height <= 0)
        {
            _frame = null;
            return;
        }

        int pixelWidth = sender.ConvertDipsToPixels((float)sender.Size.Width, CanvasDpiRounding.Round);
        int pixelHeight = sender.ConvertDipsToPixels((float)sender.Size.Height, CanvasDpiRounding.Round);
        _frame = renderer.Render(pixelWidth, pixelHeight, _state.IsPlaying);
    }

    public void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing)
    {
        if (_state.BackgroundType == BackgroundType.LikeApple && _frame is not null)
            session.DrawImage(_frame, new Rect(0, 0, sender.Size.Width, sender.Size.Height));
    }

    public async Task SetArtworkAsync(IRandomAccessStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int requestVersion = Interlocked.Increment(ref _artworkRequestVersion);
        LikeAppleBackgroundRenderer? renderer = _renderer;
        CanvasDevice? device = _device;
        if (renderer is null || device is null) return;

        CanvasBitmap? artwork = null;
        try
        {
            artwork = await CanvasBitmap.LoadAsync(
                device,
                stream,
                96f,
                CanvasAlphaMode.Premultiplied);
            if (!ReferenceEquals(renderer, _renderer) ||
                requestVersion != Volatile.Read(ref _artworkRequestVersion)) return;

            renderer.SetArtwork(artwork);
            artwork = null; // ownership moved to the renderer
        }
        finally
        {
            artwork?.Dispose();
        }
    }

    public void SetLightTheme(bool isBright)
    {
        _renderer?.SetLightTheme(isBright);
    }

    public void Dispose()
    {
        DisposeRenderer();
        _device = null;
    }

    private void DisposeRenderer()
    {
        _frame = null;
        _renderer?.Dispose();
        _renderer = null;
        _isVisible = false;
    }
}
