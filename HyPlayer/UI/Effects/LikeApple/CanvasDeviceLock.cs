using Microsoft.Graphics.Canvas;
using System;
using System.Runtime.InteropServices;
using IWinRTObject = WinRT.IWinRTObject;

namespace HyPlayer.UI.Effects.LikeApple;

internal sealed unsafe class CanvasDeviceLock
{
    private const int GetNativeResourceSlot = 3;
    private const int GetFactorySlot = 3;
    private const int EnterSlot = 4;
    private const int LeaveSlot = 5;
    private static readonly Guid ResourceWrapperInterface = new("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");
    private static readonly Guid Direct2DDeviceInterface = new("47DD575D-AC05-4CDD-8049-9B02CD16F44C");
    private static readonly Guid MultithreadInterface = new("31E6E7BC-E0FF-4D46-8C64-A0A8C41C15D3");

    private readonly CanvasDevice _device;
    private readonly nint _multithread;

    public CanvasDeviceLock(CanvasDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _multithread = GetMultithread(device);
    }

    ~CanvasDeviceLock()
    {
        if (_multithread != 0) Marshal.Release(_multithread);
    }

    public bool IsNative => _multithread != 0;

    public CanvasLock? Enter()
    {
        if (_multithread == 0) return _device.Lock();
        ((delegate* unmanaged[Stdcall]<nint, void>)(*(void***)_multithread)[EnterSlot])(_multithread);
        return null;
    }

    public void Leave(CanvasLock? held)
    {
        if (_multithread == 0)
        {
            NativeClosable.Close(held);
            return;
        }

        ((delegate* unmanaged[Stdcall]<nint, void>)(*(void***)_multithread)[LeaveSlot])(_multithread);
        GC.KeepAlive(this);
    }

    private static nint GetMultithread(CanvasDevice device)
    {
        nint wrapper = 0;
        nint direct2DDevice = 0;
        nint factory = 0;
        try
        {
            if (device is not IWinRTObject native ||
                Marshal.QueryInterface(native.NativeObject.ThisPtr, in ResourceWrapperInterface, out wrapper) < 0)
                return 0;
            fixed (Guid* deviceInterface = &Direct2DDeviceInterface)
            {
                if (((delegate* unmanaged[Stdcall]<nint, nint, float, Guid*, nint*, int>)
                        (*(void***)wrapper)[GetNativeResourceSlot])(wrapper, 0, 0, deviceInterface,
                        &direct2DDevice) < 0 || direct2DDevice == 0)
                    return 0;
            }

            ((delegate* unmanaged[Stdcall]<nint, nint*, void>)(*(void***)direct2DDevice)[GetFactorySlot])(
                direct2DDevice, &factory);
            if (factory == 0) return 0;
            return Marshal.QueryInterface(factory, in MultithreadInterface, out var multithread) < 0
                ? 0
                : multithread;
        }
        finally
        {
            if (factory != 0) Marshal.Release(factory);
            if (direct2DDevice != 0) Marshal.Release(direct2DDevice);
            if (wrapper != 0) Marshal.Release(wrapper);
            GC.KeepAlive(device);
        }
    }
}
