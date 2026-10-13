#nullable enable
using System;
using System.Runtime.InteropServices;
using WinRT;

namespace HyPlayer.UI.Effects;

internal static unsafe class NativeClosable
{
    private const int CloseSlot = 6;
    private static readonly Guid ClosableInterface = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");

    public static void Close(object? resource)
    {
        if (resource is null) return;
        if (resource is IWinRTObject { HasUnwrappableNativeObject: true } wrapper &&
            Marshal.QueryInterface(wrapper.NativeObject.ThisPtr, in ClosableInterface, out var closable) >= 0)
        {
            int result;
            try
            {
                result = ((delegate* unmanaged[Stdcall]<nint, int>)(*(void***)closable)[CloseSlot])(closable);
            }
            finally
            {
                Marshal.Release(closable);
            }

            GC.KeepAlive(resource);
            Marshal.ThrowExceptionForHR(result);
            return;
        }

        (resource as IDisposable)?.Dispose();
    }
}
