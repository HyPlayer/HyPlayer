using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using HyPlayer.Domain;
using HyPlayer.UI.Effects.LikeApple;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Storage;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
/// Vortice/D3D11 implementation of the Apple Music-inspired expanded-player backdrop.
/// </summary>
public sealed partial class LikeAppleBackgroundLayer : IExpandedCanvasLayer, IDisposable
{
    private const int MaximumArtworkDimension = 300;
    private readonly AudioGraphPlayer _player;
    private readonly ExpandedCanvasState _state;
    private CanvasDevice? _device;
    private CanvasRenderTarget? _frame;
    private LikeAppleBackgroundRenderer? _renderer;
    private Task? _rendererCreationTask;
    private bool _isVisible;
    private int _artworkRequestVersion;
    private readonly object _lifetimeGate = new();
    private int _resourceVersion;
    private bool _disposed;
    private int _renderWidth;
    private int _renderHeight;
    private int _pendingWidth;
    private int _pendingHeight;
    private long _pendingSizeTimestamp;
    private long _renderSizeTimestamp;

    private const long SurfaceQuietTicks = 150 * TimeSpan.TicksPerMillisecond;
    private const long SurfaceRepeatTicks = 300 * TimeSpan.TicksPerMillisecond;

    public LikeAppleBackgroundLayer(ExpandedCanvasState state, AudioGraphPlayer player)
    {
        _state = state;
        _player = player;
    }

    public string LayerName => "LikeAppleBackground";
    public int Order => 0;
    public bool NeedsFrames => _state.BackgroundType == BackgroundType.LikeApple;

    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        DisposeRenderer();
        if (_disposed) return;
        if (_state.BackgroundType != BackgroundType.LikeApple)
        {
            _rendererCreationTask = null;
            return;
        }
        _device = sender.Device;
        _rendererCreationTask = CreateRendererAsync(sender.Device, _resourceVersion);
        args.TrackAsyncAction(_rendererCreationTask.AsAsyncAction());
    }

    private async Task CreateRendererAsync(CanvasDevice device, int resourceVersion)
    {
        LikeAppleShaderBytecode shaderBytecode = await LoadShaderBytecodeAsync();
        lock (_lifetimeGate)
        {
            if (_disposed || resourceVersion != _resourceVersion) return;
            var renderer = new LikeAppleBackgroundRenderer(
                device,
                _player.FFTProcessor,
                shaderBytecode,
                lightTheme: _state.IsBrightTheme);
            renderer.SetIsBehindLyrics(true, animate: false);
            _renderer = renderer;
            _isVisible = false;
        }
    }

    private static async Task<LikeAppleShaderBytecode> LoadShaderBytecodeAsync()
    {
        Task<byte[]> rotationVertex = LoadShaderAsync("RotationVertex");
        Task<byte[]> artworkFillVertex = LoadShaderAsync("ArtworkFillVertex");
        Task<byte[]> fullscreenVertex = LoadShaderAsync("FullscreenVertex");
        Task<byte[]> pinchVertex = LoadShaderAsync("PinchVertex");
        Task<byte[]> rotationPixel = LoadShaderAsync("RotationPixel");
        Task<byte[]> blurHorizontalPixel = LoadShaderAsync("BlurHorizontalPixel");
        Task<byte[]> blurVerticalPixel = LoadShaderAsync("BlurVerticalPixel");
        Task<byte[]> ordinaryMaterialPixel = LoadShaderAsync("OrdinaryMaterialPixel");
        Task<byte[]> materialTreatedPixel = LoadShaderAsync("MaterialTreatedPixel");
        Task<byte[]> materialCompositePixel = LoadShaderAsync("MaterialCompositePixel");
        Task<byte[]> pinchPixel = LoadShaderAsync("PinchPixel");
        Task<byte[]> pinchCompositePixel = LoadShaderAsync("PinchCompositePixel");

        await Task.WhenAll(
            rotationVertex,
            artworkFillVertex,
            fullscreenVertex,
            pinchVertex,
            rotationPixel,
            blurHorizontalPixel,
            blurVerticalPixel,
            ordinaryMaterialPixel,
            materialTreatedPixel,
            materialCompositePixel,
            pinchPixel,
            pinchCompositePixel);
        return new LikeAppleShaderBytecode(
            await rotationVertex,
            await artworkFillVertex,
            await fullscreenVertex,
            await pinchVertex,
            await rotationPixel,
            await blurHorizontalPixel,
            await blurVerticalPixel,
            await ordinaryMaterialPixel,
            await materialTreatedPixel,
            await materialCompositePixel,
            await pinchPixel,
            await pinchCompositePixel);
    }

    private static async Task<byte[]> LoadShaderAsync(string shaderName)
    {
        StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(
            new Uri($"ms-appx:///Shaders/LikeApple/{shaderName}.bin"));
        await using Stream stream = await file.OpenStreamForReadAsync();
        if (stream.Length > int.MaxValue)
        {
            throw new IOException($"Shader '{shaderName}' is too large.");
        }

        byte[] bytecode = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytecode);
        return bytecode;
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

        Size size = _state.Viewport(sender.Size);
        if (size.Width <= 0 || size.Height <= 0)
        {
            _frame = null;
            return;
        }

        int pixelWidth = sender.ConvertDipsToPixels((float)size.Width, CanvasDpiRounding.Round);
        int pixelHeight = sender.ConvertDipsToPixels((float)size.Height, CanvasDpiRounding.Round);
        long now = Stopwatch.GetTimestamp();
        if (pixelWidth != _pendingWidth || pixelHeight != _pendingHeight)
        {
            _pendingWidth = pixelWidth;
            _pendingHeight = pixelHeight;
            _pendingSizeTimestamp = now;
        }

        if ((pixelWidth != _renderWidth || pixelHeight != _renderHeight) &&
            (_renderWidth <= 0 || _renderHeight <= 0 ||
             Stopwatch.GetElapsedTime(_pendingSizeTimestamp, now).Ticks >= SurfaceQuietTicks ||
             Stopwatch.GetElapsedTime(_renderSizeTimestamp, now).Ticks >= SurfaceRepeatTicks))
        {
            _renderWidth = pixelWidth;
            _renderHeight = pixelHeight;
            _renderSizeTimestamp = now;
        }

        _frame = renderer.Render(_renderWidth, _renderHeight, _state.IsPlaying);
    }

    public void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing)
    {
        CanvasRenderTarget? frame = _frame;
        if (_state.BackgroundType != BackgroundType.LikeApple || frame is null) return;
        Size canvas = sender.Size;
        Size viewport = _state.Viewport(canvas);
        session.DrawImage(frame, new Rect(0, 0, viewport.Width, viewport.Height));
        double right = canvas.Width - viewport.Width;
        double bottom = canvas.Height - viewport.Height;
        if (right <= 0 && bottom <= 0) return;
        Size source = frame.Size;
        double pixel = 96 / frame.Dpi;
        if (right > 0)
            session.DrawImage(frame, new Rect(viewport.Width, 0, right, viewport.Height),
                new Rect(source.Width - pixel, 0, pixel, source.Height));
        if (bottom > 0)
            session.DrawImage(frame, new Rect(0, viewport.Height, viewport.Width, bottom),
                new Rect(0, source.Height - pixel, source.Width, pixel));
        if (right > 0 && bottom > 0)
            session.DrawImage(frame, new Rect(viewport.Width, viewport.Height, right, bottom),
                new Rect(source.Width - pixel, source.Height - pixel, pixel, pixel));
    }

    public async Task SetArtworkAsync(IRandomAccessStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int requestVersion = Interlocked.Increment(ref _artworkRequestVersion);
        Task? rendererCreationTask = _rendererCreationTask;
        if (rendererCreationTask is not null)
        {
            await rendererCreationTask;
        }
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
            artwork = ResizeArtwork(device, artwork);
            lock (_lifetimeGate)
            {
                if (_disposed || !ReferenceEquals(renderer, _renderer) ||
                    requestVersion != Volatile.Read(ref _artworkRequestVersion)) return;

                renderer.SetArtwork(artwork);
                artwork = null; // ownership moved to the renderer
            }
        }
        finally
        {
            artwork?.Dispose();
        }
    }

    private static CanvasBitmap ResizeArtwork(CanvasDevice device, CanvasBitmap artwork)
    {
        double width = artwork.SizeInPixels.Width;
        double height = artwork.SizeInPixels.Height;
        double longestDimension = Math.Max(width, height);
        if (longestDimension <= MaximumArtworkDimension)
        {
            return artwork;
        }

        double scale = MaximumArtworkDimension / longestDimension;
        float resizedWidth = Math.Max(1f, (float)Math.Round(
            width * scale,
            MidpointRounding.AwayFromZero));
        float resizedHeight = Math.Max(1f, (float)Math.Round(
            height * scale,
            MidpointRounding.AwayFromZero));
        var resized = new CanvasRenderTarget(
            device,
            resizedWidth,
            resizedHeight,
            96f,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            CanvasAlphaMode.Premultiplied);
        try
        {
            using CanvasDrawingSession session = resized.CreateDrawingSession();
            session.DrawImage(
                artwork,
                new Rect(0, 0, resizedWidth, resizedHeight),
                artwork.Bounds,
                1f,
                CanvasImageInterpolation.HighQualityCubic);
        }
        catch
        {
            resized.Dispose();
            throw;
        }

        artwork.Dispose();
        return resized;
    }

    public void SetLightTheme(bool isBright)
    {
        lock (_lifetimeGate)
            _renderer?.SetLightTheme(isBright);
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            _disposed = true;
            DisposeRenderer();
            _device = null;
            _rendererCreationTask = null;
        }
    }

    private void DisposeRenderer()
    {
        lock (_lifetimeGate)
        {
            _resourceVersion++;
            Interlocked.Increment(ref _artworkRequestVersion);
            _frame = null;
            _renderWidth = 0;
            _renderHeight = 0;
            _renderer?.Dispose();
            _renderer = null;
            _isVisible = false;
        }
    }
}
