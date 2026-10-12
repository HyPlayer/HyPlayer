#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Platform.Storage;

public static class FolderAccess
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<string, bool> AccessCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsUsable(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (AccessCache.TryGetValue(path, out var known)) return known;
        var probe = ProbeAsync(path);
        return probe.Wait(ProbeTimeout) && probe.Result;
    }

    public static async Task<bool> ProbeAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (AccessCache.TryGetValue(path, out var known)) return known;
        var usable = await CheckAsync(path).ConfigureAwait(false);
        AccessCache[path] = usable;
        return usable;
    }

    public static void Forget(string? path)
    {
        if (!string.IsNullOrEmpty(path)) AccessCache.TryRemove(path, out _);
    }

    public static event Action<string>? AccessLost;

    public static async Task<StorageFolder?> OpenAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !await ProbeAsync(path).ConfigureAwait(false)) return null;
        try
        {
            return await StorageFolder.GetFolderFromPathAsync(path).AsTask().ConfigureAwait(false);
        }
        catch
        {
            if (AccessCache.TryUpdate(path, false, true)) NotifyLost(path);
            return null;
        }
    }

    private static void NotifyLost(string path)
    {
        try
        {
            AccessLost?.Invoke(path);
        }
        catch
        {
        }
    }

    public static bool IsSamePath(string? a, string? b)
    {
        return a is not null && b is not null &&
               string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckAsync(string path)
    {
        try
        {
            var packageRoot = Path.GetDirectoryName(ApplicationData.Current.LocalFolder.Path);
            if (IsUnder(path, packageRoot)) return true;
            if (IsUnder(path, Path.GetDirectoryName(packageRoot))) return false;
            await StorageFolder.GetFolderFromPathAsync(path).AsTask().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string? root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var child = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
        var parent = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
