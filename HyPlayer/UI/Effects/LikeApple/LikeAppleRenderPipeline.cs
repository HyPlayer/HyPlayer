using System;
using System.Numerics;
using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using Format = Vortice.DXGI.Format;

namespace HyPlayer.UI.Effects.LikeApple;

/// <summary>
/// Owns the D3D11 pipeline state used by the Apple Music-inspired renderer.
/// Shader I/O remains outside this class so pipeline construction is synchronous.
/// </summary>
internal sealed unsafe partial class LikeAppleRenderPipeline : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;

    private ID3D11VertexShader _rotationVertexShader = null!;
    private ID3D11VertexShader _artworkFillVertexShader = null!;
    private ID3D11VertexShader _fullscreenVertexShader = null!;
    private ID3D11VertexShader _pinchVertexShader = null!;
    private ID3D11PixelShader _rotationPixelShader = null!;
    private ID3D11PixelShader _horizontalBlurPixelShader = null!;
    private ID3D11PixelShader _verticalBlurPixelShader = null!;
    private ID3D11PixelShader _ordinaryMaterialPixelShader = null!;
    private ID3D11PixelShader _materialTreatedPixelShader = null!;
    private ID3D11PixelShader _materialCompositePixelShader = null!;
    private ID3D11PixelShader _pinchPixelShader = null!;
    private ID3D11PixelShader _pinchCompositePixelShader = null!;
    private ID3D11InputLayout _quadInputLayout = null!;
    private ID3D11InputLayout _pinchInputLayout = null!;
    private ID3D11Buffer _quadVertexBuffer = null!;
    private ID3D11Buffer _quadIndexBuffer = null!;
    private ID3D11Buffer _pinchVertexBuffer = null!;
    private ID3D11Buffer _pinchIndexBuffer = null!;
    private ID3D11Buffer _frameConstantBuffer = null!;
    private ID3D11SamplerState _linearClampSampler = null!;
    private ID3D11SamplerState _linearZeroBorderSampler = null!;
    private ID3D11RasterizerState _rasterizerState = null!;
    private int _pinchIndexCount;
    private bool _disposed;

    public LikeAppleRenderPipeline(
        CanvasDevice canvasDevice,
        LikeAppleShaderBytecode shaderBytecode,
        LikeApplePinchVertex[] pinchVertices,
        ushort[] pinchIndices)
    {
        _device = Win2DDirect3DBridge.GetDirect3DDevice(canvasDevice);
        _context = _device.ImmediateContext;
        try
        {
            CreateResources(shaderBytecode, pinchVertices, pinchIndices);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public LikeAppleGpuArtwork CreateArtwork(CanvasBitmap bitmap)
    {
        ID3D11Texture2D texture = Win2DDirect3DBridge.GetTexture2D(bitmap);
        ID3D11ShaderResourceView? view = null;
        try
        {
            view = _device.CreateShaderResourceView(texture);
            return new LikeAppleGpuArtwork(bitmap, texture, view);
        }
        catch
        {
            view?.Dispose();
            texture.Dispose();
            bitmap.Dispose();
            throw;
        }
    }

    public LikeAppleRenderSurface CreateSurface(int width, int height)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R16G16B16A16_Float,
            SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };
        ID3D11Texture2D texture = _device.CreateTexture2D(description);
        ID3D11RenderTargetView? renderTarget = null;
        ID3D11ShaderResourceView? shaderResource = null;
        try
        {
            renderTarget = _device.CreateRenderTargetView(texture);
            shaderResource = _device.CreateShaderResourceView(texture);
            return new LikeAppleRenderSurface(
                texture,
                renderTarget,
                shaderResource,
                width,
                height);
        }
        catch
        {
            shaderResource?.Dispose();
            renderTarget?.Dispose();
            texture.Dispose();
            throw;
        }
    }

    public LikeAppleRenderSurface CreateOutputSurface(
        CanvasRenderTarget outputTarget,
        int width,
        int height)
    {
        ID3D11Texture2D? texture = Win2DDirect3DBridge.GetTexture2D(outputTarget);
        try
        {
            ID3D11RenderTargetView renderTarget = _device.CreateRenderTargetView(texture);
            var surface = new LikeAppleRenderSurface(
                texture,
                renderTarget,
                null,
                width,
                height);
            texture = null;
            return surface;
        }
        finally
        {
            texture?.Dispose();
        }
    }

    public void UpdatePinchMesh(
        LikeApplePinchVertex[] pinchVertices,
        ushort[] pinchIndices)
    {
        ID3D11Buffer replacementVertices = _device.CreateBuffer(
            pinchVertices,
            BindFlags.VertexBuffer);
        ID3D11Buffer replacementIndices;
        try
        {
            replacementIndices = _device.CreateBuffer(
                pinchIndices,
                BindFlags.IndexBuffer);
        }
        catch
        {
            replacementVertices.Dispose();
            throw;
        }

        ID3D11Buffer previousVertices = _pinchVertexBuffer;
        ID3D11Buffer previousIndices = _pinchIndexBuffer;
        _pinchVertexBuffer = replacementVertices;
        _pinchIndexBuffer = replacementIndices;
        _pinchIndexCount = pinchIndices.Length;
        previousVertices.Dispose();
        previousIndices.Dispose();
    }

    public void PrepareFrame()
    {
        _context.VSSetConstantBuffers(0, 1, [_frameConstantBuffer]);
        _context.PSSetConstantBuffers(0, 1, [_frameConstantBuffer]);
        _context.PSSetSamplers(
            0,
            2,
            [_linearClampSampler, _linearZeroBorderSampler]);
        _context.RSSetState(_rasterizerState);
    }

    public void UpdateConstants(in LikeAppleFrameConstants constants)
    {
        _context.UpdateSubresource(in constants, _frameConstantBuffer);
    }

    public void RenderBackdrop(
        LikeAppleGpuArtwork currentArtwork,
        LikeAppleGpuArtwork? previousArtwork,
        LikeAppleRenderSurface rotationSurface,
        LikeAppleRenderSurface horizontalBlurSurface,
        LikeAppleRenderSurface verticalBlurSurface)
    {
        RenderRotationPass(currentArtwork, previousArtwork, rotationSurface);
        RenderBlurPass(
            RequireShaderResource(rotationSurface),
            horizontalBlurSurface,
            _horizontalBlurPixelShader);
        RenderBlurPass(
            RequireShaderResource(horizontalBlurSurface),
            verticalBlurSurface,
            _verticalBlurPixelShader);
    }

    public void RenderComposite(
        LikeAppleRenderSurface lyricsBackdrop,
        LikeAppleRenderSurface ordinaryBackdrop,
        LikeAppleRenderSurface outputSurface,
        bool isVerticalLayout,
        float lyricsModeMix)
    {
        SetViewport(outputSurface.Width, outputSurface.Height);
        SetRenderTarget(outputSurface.RenderTargetView);
        _context.ClearRenderTargetView(
            outputSurface.RenderTargetView,
            new Color4(0f, 0f, 0f, 1f));

        if (lyricsModeMix <= 0f)
        {
            BindPixelShaderResources(RequireShaderResource(ordinaryBackdrop));
            DrawFullscreenMaterial(_ordinaryMaterialPixelShader);
            return;
        }

        if (lyricsModeMix >= 1f)
        {
            BindPixelShaderResources(RequireShaderResource(lyricsBackdrop));
            if (isVerticalLayout)
            {
                DrawFullscreenMaterial(_materialTreatedPixelShader);
            }
            DrawPinchMesh(_pinchPixelShader);
            return;
        }

        BindPixelShaderResources(
            RequireShaderResource(lyricsBackdrop),
            RequireShaderResource(ordinaryBackdrop));
        DrawFullscreenMaterial(_materialCompositePixelShader);
        DrawPinchMesh(_pinchCompositePixelShader);
    }

    public void CompleteFrame()
    {
        UnbindPixelShaderResources(2);
        SetRenderTarget(null);
    }

    public void ThrowIfDeviceRemoved()
    {
        var removedReason = _device.DeviceRemovedReason;
        if (removedReason.Failure)
        {
            removedReason.CheckError();
        }
    }

    public void ClearState() => _context.ClearState();

    public void ClearAndFlush()
    {
        _context.ClearState();
        _context.Flush();
    }

    private void CreateResources(
        LikeAppleShaderBytecode shaderBytecode,
        LikeApplePinchVertex[] pinchVertices,
        ushort[] pinchIndices)
    {
        _rotationVertexShader = CreateVertexShader(shaderBytecode.RotationVertex);
        _artworkFillVertexShader = CreateVertexShader(shaderBytecode.ArtworkFillVertex);
        _fullscreenVertexShader = CreateVertexShader(shaderBytecode.FullscreenVertex);
        _pinchVertexShader = CreateVertexShader(shaderBytecode.PinchVertex);
        _rotationPixelShader = CreatePixelShader(shaderBytecode.RotationPixel);
        _horizontalBlurPixelShader = CreatePixelShader(shaderBytecode.BlurHorizontalPixel);
        _verticalBlurPixelShader = CreatePixelShader(shaderBytecode.BlurVerticalPixel);
        _ordinaryMaterialPixelShader = CreatePixelShader(shaderBytecode.OrdinaryMaterialPixel);
        _materialTreatedPixelShader = CreatePixelShader(shaderBytecode.MaterialTreatedPixel);
        _materialCompositePixelShader = CreatePixelShader(shaderBytecode.MaterialCompositePixel);
        _pinchPixelShader = CreatePixelShader(shaderBytecode.PinchPixel);
        _pinchCompositePixelShader = CreatePixelShader(shaderBytecode.PinchCompositePixel);

        _quadInputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
        ],
            shaderBytecode.RotationVertex);
        _pinchInputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("FROMPOS", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TOPOS", 0, Format.R32G32_Float, 8, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
        ],
            shaderBytecode.PinchVertex);

        LikeAppleQuadVertex[] quadVertices =
        [
            new(new Vector4(-1f, -1f, 0f, 1f), new Vector2(0f, 1f)),
            new(new Vector4(-1f, 1f, 0f, 1f), new Vector2(0f, 0f)),
            new(new Vector4(1f, 1f, 0f, 1f), new Vector2(1f, 0f)),
            new(new Vector4(1f, -1f, 0f, 1f), new Vector2(1f, 1f)),
        ];
        ushort[] quadIndices = [0, 1, 2, 2, 3, 0];
        _quadVertexBuffer = _device.CreateBuffer(quadVertices, BindFlags.VertexBuffer);
        _quadIndexBuffer = _device.CreateBuffer(quadIndices, BindFlags.IndexBuffer);
        _pinchVertexBuffer = _device.CreateBuffer(pinchVertices, BindFlags.VertexBuffer);
        _pinchIndexBuffer = _device.CreateBuffer(pinchIndices, BindFlags.IndexBuffer);
        _pinchIndexCount = pinchIndices.Length;
        _frameConstantBuffer = _device.CreateBuffer(
            (uint)sizeof(LikeAppleFrameConstants),
            BindFlags.ConstantBuffer,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);

        _linearClampSampler = CreateLinearSampler(TextureAddressMode.Clamp);
        _linearZeroBorderSampler = CreateLinearSampler(TextureAddressMode.Border);
        _rasterizerState = _device.CreateRasterizerState(
            new RasterizerDescription(
                CullMode.None,
                FillMode.Solid));
    }

    private ID3D11SamplerState CreateLinearSampler(TextureAddressMode addressMode)
    {
        return _device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear,
            addressMode,
            0f,
            1,
            ComparisonFunction.Never,
            0f,
            float.MaxValue));
    }

    private void RenderRotationPass(
        LikeAppleGpuArtwork currentArtwork,
        LikeAppleGpuArtwork? previousArtwork,
        LikeAppleRenderSurface rotationSurface)
    {
        SetViewport(rotationSurface.Width, rotationSurface.Height);
        SetRenderTarget(rotationSurface.RenderTargetView);
        _context.ClearRenderTargetView(
            rotationSurface.RenderTargetView,
            new Color4(0f, 0f, 0f, 1f));
        _context.IASetInputLayout(_quadInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        BindVertexBuffer(_quadVertexBuffer, sizeof(LikeAppleQuadVertex));
        _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
        _context.VSSetShader(_rotationVertexShader, null, 0);
        _context.PSSetShader(_rotationPixelShader, null, 0);
        BindPixelShaderResources(
            currentArtwork.ShaderResourceView,
            previousArtwork?.ShaderResourceView ?? currentArtwork.ShaderResourceView);

        _context.VSSetShader(_artworkFillVertexShader, null, 0);
        _context.DrawIndexed(6, 0, 0);
        _context.VSSetShader(_rotationVertexShader, null, 0);
        _context.DrawIndexedInstanced(6, 3, 0, 0, 0);
        UnbindPixelShaderResources(2);
    }

    private void RenderBlurPass(
        ID3D11ShaderResourceView source,
        LikeAppleRenderSurface target,
        ID3D11PixelShader shader)
    {
        SetViewport(target.Width, target.Height);
        SetRenderTarget(target.RenderTargetView);
        _context.IASetInputLayout(_quadInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        BindVertexBuffer(_quadVertexBuffer, sizeof(LikeAppleQuadVertex));
        _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
        _context.VSSetShader(_fullscreenVertexShader, null, 0);
        _context.PSSetShader(shader, null, 0);
        BindPixelShaderResources(source);
        _context.DrawIndexed(6, 0, 0);
        UnbindPixelShaderResources(1);
    }

    private void DrawFullscreenMaterial(ID3D11PixelShader pixelShader)
    {
        _context.IASetInputLayout(_quadInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        BindVertexBuffer(_quadVertexBuffer, sizeof(LikeAppleQuadVertex));
        _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
        _context.VSSetShader(_fullscreenVertexShader, null, 0);
        _context.PSSetShader(pixelShader, null, 0);
        _context.DrawIndexed(6, 0, 0);
    }

    private void DrawPinchMesh(ID3D11PixelShader pixelShader)
    {
        _context.IASetInputLayout(_pinchInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        BindVertexBuffer(_pinchVertexBuffer, sizeof(LikeApplePinchVertex));
        _context.IASetIndexBuffer(_pinchIndexBuffer, Format.R16_UInt, 0);
        _context.VSSetShader(_pinchVertexShader, null, 0);
        _context.PSSetShader(pixelShader, null, 0);
        _context.DrawIndexed((uint)_pinchIndexCount, 0, 0);
    }

    private ID3D11VertexShader CreateVertexShader(byte[] bytecode) =>
        _device.CreateVertexShader(bytecode.AsSpan());

    private ID3D11PixelShader CreatePixelShader(byte[] bytecode) =>
        _device.CreatePixelShader(bytecode.AsSpan());

    private void SetRenderTarget(ID3D11RenderTargetView? renderTarget)
    {
        if (renderTarget is null)
        {
            _context.OMSetRenderTargets(
                0,
                Array.Empty<ID3D11RenderTargetView>(),
                null);
            return;
        }
        _context.OMSetRenderTargets(1, [renderTarget], null);
    }

    private void SetViewport(int width, int height)
    {
        var viewport = new Viewport(width, height);
        _context.RSSetViewports([viewport]);
    }

    private void BindVertexBuffer(ID3D11Buffer buffer, int stride)
    {
        _context.IASetVertexBuffer(0, buffer, (uint)stride);
    }

    private void BindPixelShaderResources(params ID3D11ShaderResourceView[] resources)
    {
        for (uint index = 0; index < (uint)resources.Length; index++)
        {
            _context.PSSetShaderResource(index, resources[index]);
        }
    }

    private void UnbindPixelShaderResources(int count)
    {
        for (uint index = 0; index < (uint)count; index++)
        {
            _context.PSSetShaderResource(index, null!);
        }
    }

    private static ID3D11ShaderResourceView RequireShaderResource(
        LikeAppleRenderSurface surface) =>
        surface.ShaderResourceView ??
        throw new InvalidOperationException("The render surface has no shader resource view.");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _rasterizerState?.Dispose();
        _linearZeroBorderSampler?.Dispose();
        _linearClampSampler?.Dispose();
        _frameConstantBuffer?.Dispose();
        _pinchIndexBuffer?.Dispose();
        _pinchVertexBuffer?.Dispose();
        _quadIndexBuffer?.Dispose();
        _quadVertexBuffer?.Dispose();
        _pinchInputLayout?.Dispose();
        _quadInputLayout?.Dispose();
        _pinchCompositePixelShader?.Dispose();
        _pinchPixelShader?.Dispose();
        _materialCompositePixelShader?.Dispose();
        _materialTreatedPixelShader?.Dispose();
        _ordinaryMaterialPixelShader?.Dispose();
        _verticalBlurPixelShader?.Dispose();
        _horizontalBlurPixelShader?.Dispose();
        _rotationPixelShader?.Dispose();
        _pinchVertexShader?.Dispose();
        _fullscreenVertexShader?.Dispose();
        _artworkFillVertexShader?.Dispose();
        _rotationVertexShader?.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
