using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HyPlayer.UWP.Chopin.Utils
{
    internal sealed unsafe partial class PreciseWait : IDisposable
    {
        private const uint HighResolution = 0x2;
        private const uint TimerAccess = 0x1F0003;
        private nint _timer;

        public PreciseWait()
        {
            try
            {
                _timer = CreateWaitableTimerExW(0, 0, HighResolution, TimerAccess);
            }
            catch
            {
                _timer = 0;
            }
        }

        public void Wait(int milliseconds)
        {
            var timer = _timer;
            if (timer != 0)
            {
                var due = -milliseconds * 10_000L;
                if (SetWaitableTimerEx(timer, &due, 0, 0, 0, 0, 0) != 0)
                {
                    WaitForSingleObjectEx(timer, (uint)milliseconds + 50, 0);
                    return;
                }
            }

            Thread.Sleep(milliseconds);
        }

        public void Dispose()
        {
            var timer = Interlocked.Exchange(ref _timer, 0);
            if (timer != 0) CloseHandle(timer);
        }

        [LibraryImport("kernel32.dll")]
        private static partial nint CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);

        [LibraryImport("kernel32.dll")]
        private static partial int SetWaitableTimerEx(nint timer, long* dueTime, int period, nint completion, nint argument, nint context, uint tolerance);

        [LibraryImport("kernel32.dll")]
        private static partial uint WaitForSingleObjectEx(nint handle, uint milliseconds, int alertable);

        [LibraryImport("kernel32.dll")]
        private static partial int CloseHandle(nint handle);
    }
}
