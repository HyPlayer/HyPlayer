using System;
using Microsoft.Graphics.Canvas;
using Vortice.Direct3D11;

namespace HyPlayer.UI.Effects.LikeApple;

internal sealed partial class LikeAppleGpuArtwork : IDisposable
{
    public LikeAppleGpuArtwork(
        CanvasBitmap bitmap,
        ID3D11Texture2D texture,
        ID3D11ShaderResourceView shaderResourceView)
    {
        Bitmap = bitmap;
        Texture = texture;
        ShaderResourceView = shaderResourceView;
    }

    public CanvasBitmap Bitmap { get; }
    public ID3D11Texture2D Texture { get; }
    public ID3D11ShaderResourceView ShaderResourceView { get; }

    public void Dispose()
    {
        ShaderResourceView.Dispose();
        Texture.Dispose();
        Bitmap.Dispose();
    }
}
