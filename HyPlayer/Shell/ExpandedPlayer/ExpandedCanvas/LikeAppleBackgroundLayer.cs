using System;
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
        if (_state.BackgroundType != BackgroundType.LikeApple)
        {
            _rendererCreationTask = null;
            return;
        }
        _device = sender.Device;
        _rendererCreationTask = CreateRendererAsync(sender.Device);
        args.TrackAsyncAction(_rendererCreationTask.AsAsyncAction());
    }

    private async Task CreateRendererAsync(CanvasDevice device)
    {
        LikeAppleShaderBytecode shaderBytecode = await LoadShaderBytecodeAsync();
        var renderer = new LikeAppleBackgroundRenderer(
            device,
            _player.FFTProcessor,
            shaderBytecode,
            lightTheme: _state.IsBrightTheme);
        renderer.SetIsBehindLyrics(true, animate: false);
        _renderer = renderer;
        _isVisible = false;
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
