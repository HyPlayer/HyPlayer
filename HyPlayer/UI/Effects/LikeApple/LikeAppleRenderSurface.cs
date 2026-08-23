using System;
using Vortice.Direct3D11;

namespace HyPlayer.UI.Effects.LikeApple;

internal sealed partial class LikeAppleRenderSurface : IDisposable
{
    public LikeAppleRenderSurface(
        ID3D11Texture2D texture,
        ID3D11RenderTargetView renderTargetView,
        ID3D11ShaderResourceView? shaderResourceView,
        int width,
        int height)
    {
        Texture = texture;
        RenderTargetView = renderTargetView;
        ShaderResourceView = shaderResourceView;
        Width = width;
        Height = height;
    }

    public ID3D11Texture2D Texture { get; }
    public ID3D11RenderTargetView RenderTargetView { get; }
    public ID3D11ShaderResourceView? ShaderResourceView { get; }
    public int Width { get; }
    public int Height { get; }

    public void Dispose()
    {
        ShaderResourceView?.Dispose();
        RenderTargetView.Dispose();
        Texture.Dispose();
    }
}
