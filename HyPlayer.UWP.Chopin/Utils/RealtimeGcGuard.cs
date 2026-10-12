using System;
using System.Runtime;
using System.Threading;

namespace HyPlayer.UWP.Chopin.Utils
{
    internal static class RealtimeGcGuard
    {
        private const int LimitMilliseconds = 120_000;
        private static readonly Lock Gate = new();
        private static bool _wanted;
        private static bool _held;
        private static GCLatencyMode _restore;
        private static Timer _expiry;

        public static void Set(bool wanted)
        {
            lock (Gate)
            {
                if (wanted == _wanted) return;
                _wanted = wanted;
                if (wanted) Enter();
                else Leave();
            }
        }

        private static void Enter()
        {
            try
            {
                var current = GCSettings.LatencyMode;
                if (current is GCLatencyMode.LowLatency or GCLatencyMode.NoGCRegion) return;
                GCSettings.LatencyMode = GCLatencyMode.LowLatency;
                _restore = current;
                _held = true;
            }
            catch
            {
                return;
            }
            _expiry = new Timer(_ =>
            {
                lock (Gate) Leave();
            }, null, LimitMilliseconds, Timeout.Infinite);
        }

        private static void Leave()
        {
            _expiry?.Dispose();
            _expiry = null;
            if (!_held) return;
            _held = false;
            try
            {
                if (GCSettings.LatencyMode == GCLatencyMode.LowLatency) GCSettings.LatencyMode = _restore;
            }
            catch
            {
            }
        }
    }
}
