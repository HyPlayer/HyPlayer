using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace HyPlayer.UWP.Chopin.Utils
{
    internal sealed unsafe partial class PreciseWait : IDisposable
    {
        private const uint HighResolution = 0x2;
        private const uint TimerAccess = 0x1F0003;
        private readonly SafeWaitHandle _timer = CreateWaitableTimerExW(0, 0, HighResolution, TimerAccess);

        public void Wait(int milliseconds)
        {
            if (!_timer.IsInvalid && !_timer.IsClosed)
            {
                var due = -milliseconds * 10_000L;
                if (SetWaitableTimerEx(_timer, &due, 0, 0, 0, 0, 0) != 0)
                {
                    WaitForSingleObjectEx(_timer, (uint)milliseconds + 50, 0);
                    return;
                }
            }

            Thread.Sleep(milliseconds);
        }

        public void Dispose() => _timer.Dispose();

        [LibraryImport("kernel32.dll")]
        private static partial SafeWaitHandle CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);

        [LibraryImport("kernel32.dll")]
        private static partial int SetWaitableTimerEx(SafeWaitHandle timer, long* dueTime, int period, nint completion, nint argument, nint context, uint tolerance);

        [LibraryImport("kernel32.dll")]
        private static partial uint WaitForSingleObjectEx(SafeWaitHandle handle, uint milliseconds, int alertable);
    }
}
