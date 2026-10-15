#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Platform.Storage;

public static class FolderAccess
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private static readonly AsyncFolderAccessCache AccessCache = new(CheckAsync, ProbeTimeout);

    public static bool IsUsable(string? path)
    {
        return AccessCache.IsUsable(path);
    }

    public static Task<bool> ProbeAsync(string? path) => AccessCache.ProbeAsync(path);

    public static void Forget(string? path)
    {
        AccessCache.Forget(path);
    }

    public static event Action<string>? AccessLost;

    public static async Task<StorageFolder?> OpenAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !await ProbeAsync(path).ConfigureAwait(false)) return null;
        try
        {
            using var cancellation = new CancellationTokenSource(ProbeTimeout);
            return await StorageFolder.GetFolderFromPathAsync(path).AsTask(cancellation.Token)
                .WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            if (AccessCache.MarkUnusable(path)) NotifyLost(path);
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

    private static async Task<bool> CheckAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var packageRoot = Path.GetDirectoryName(ApplicationData.Current.LocalFolder.Path);
            if (IsUnder(path, packageRoot)) return true;
            if (IsUnder(path, Path.GetDirectoryName(packageRoot))) return false;
            await StorageFolder.GetFolderFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
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
