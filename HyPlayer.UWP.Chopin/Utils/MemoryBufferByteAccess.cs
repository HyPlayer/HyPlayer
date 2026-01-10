using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT;

namespace HyPlayer.UWP.Chopin.Utils
{
    public static class MemoryBufferByteAccessExtension
    {
#if NET
        private static ref readonly Guid IID_IMemoryBufferByteAccess
        {
            get
            {
                // 5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D
                ReadOnlySpan<byte> data = [53, 50, 13, 91, 186, 77, 68, 77, 134, 94, 143, 29, 14, 79, 208, 77];
                return ref Unsafe.As<byte, Guid>(ref MemoryMarshal.GetReference(data));
            }
        }

#else
            private static readonly Guid IID_IMemoryBufferByteAccess = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
#endif

        public static unsafe int GetBuffer(this IMemoryBufferReference reference, out byte* buffer, out uint capacity)
        {
            buffer = null;
            capacity = 0;
            if (reference is IWinRTObject winrtObj)
            {
                var hr = winrtObj.NativeObject.TryAs(IID_IMemoryBufferByteAccess, out var thisPtr);
                if (hr == 0)
                {
                    try
                    {
                        fixed (byte** pBuffer = &buffer)
                        fixed (uint* pCapacity = &capacity)
                        {
                            return ((delegate* unmanaged[Stdcall, MemberFunction]<nint, byte**, uint*, int>)(*(void***)thisPtr)[3])(thisPtr, pBuffer, pCapacity);
                        }
                    }
                    finally
                    {
                        Marshal.Release(thisPtr);
                    }
                }
                return hr;
            }
            return unchecked((int)0x80070057);
        }
    }
}
