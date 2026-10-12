#nullable enable
using System;
using Windows.System;
using Windows.System.Profile;

namespace HyPlayer.Platform.Runtime;

internal static class InteropCollectionBudget
{
    private const int ReservedBytes = 192 * 1024 * 1024;
    private const ulong MinimumMemoryLimit = 2048UL * 1024 * 1024;
    private static byte[]? _reserved;

    public static bool IsRaised => _reserved is not null;

    public static void Raise()
    {
        if (_reserved is not null) return;
        try
        {
            if (AnalyticsInfo.VersionInfo.DeviceFamily != "Windows.Desktop") return;
            if (MemoryManager.AppMemoryUsageLimit < MinimumMemoryLimit) return;
            _reserved = GC.AllocateUninitializedArray<byte>(ReservedBytes, true);
        }
        catch
        {
        }
    }
}
