using Microsoft.Graphics.Canvas;
using System;
using Vanara.PInvoke;
using Vortice.Direct3D11;
using IWinRTObject = WinRT.IWinRTObject;

namespace HyPlayer.UI.Effects.LikeApple;

/// <summary>
/// Bridges Win2D resources to their native Direct2D/Direct3D resources.
/// All returned Vortice objects own a COM reference and must be disposed.
/// </summary>
public static class Win2DDirect3DBridge
{
    public static ID3D11Device GetDirect3DDevice(CanvasDevice canvasDevice)
    {
        ArgumentNullException.ThrowIfNull(canvasDevice);

        HRESULT result = (canvasDevice as IWinRTObject).NativeObject.TryAs(typeof(IDirect3DDxgiInterfaceAccess).GUID, out var pointer);

        if (result == HRESULT.S_OK)
        {
            using var access = new IDirect3DDxgiInterfaceAccess(pointer);
            return access.GetInterface<ID3D11Device>();
        }
        throw new InvalidOperationException("Failed to obtain Direct3D device from Win2D canvas device.");
    }

    public static ID3D11Texture2D GetTexture2D(CanvasBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        HRESULT success = (bitmap as IWinRTObject).NativeObject.TryAs(typeof(IDirect3DDxgiInterfaceAccess).GUID, out var pointer);

        if (success == HRESULT.S_OK)
        {
            using var access = new IDirect3DDxgiInterfaceAccess(pointer);
            return access.GetInterface<ID3D11Texture2D>();
        }

        throw new InvalidOperationException("Failed to obtain Direct3D texture from Win2D bitmap.");
    }
}
