#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using HyPlayer.Application;
using HyPlayer.Domain.Settings;

namespace HyPlayer.Platform.Storage.Cache;

public sealed record StorageItemInfo(StorageFile File, string RelativeFolder, ulong Size, DateTimeOffset Modified);

public static class StorageMaintenance
{
    private static readonly string[] _cacheAudioExtensions = [".flac", ".mp3", ".m4a", ".ogg", ".aac", ".wav"];

    private static readonly HashSet<string> _cacheFolderNames =
        new(Enum.GetNames<CacheType>(), StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim _trimLock = new(1, 1);
    private static int _trimScheduled;
    private static DateTime _lastTrim = DateTime.MinValue;

    public static bool IsCacheFolderName(string name)
    {
        return _cacheFolderNames.Contains(name);
    }

    private static bool IsCacheAudioFile(string name)
    {
        var extension = Path.GetExtension(name);
        if (Array.FindIndex(_cacheAudioExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)) < 0)
            return false;

        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 0 && stem.All(char.IsAsciiDigit);
    }

    private static async Task<IReadOnlyList<StorageFile>> GetFilesWithPropertiesAsync(StorageFolder folder)
    {
        var options = new QueryOptions { FolderDepth = FolderDepth.Shallow };
        options.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, []);
        return await folder.CreateFileQueryWithOptions(options).GetFilesAsync();
    }

    public static async Task<List<StorageItemInfo>> CollectCacheAsync(
        StorageFolder root,
        CancellationToken ct = default)
    {
        var result = new List<StorageItemInfo>();
        await AddFilesAsync(root, "", IsCacheAudioFile).ConfigureAwait(false);
        foreach (var folder in await root.GetFoldersAsync())
        {
            if (!IsCacheFolderName(folder.Name)) continue;
            await AddFilesAsync(folder, folder.Name, _ => true).ConfigureAwait(false);
        }

        return result;

        async Task AddFilesAsync(StorageFolder folder, string relative, Func<string, bool> filter)
        {
            foreach (var file in await GetFilesWithPropertiesAsync(folder).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                if (!filter(file.Name)) continue;
                try
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    result.Add(new StorageItemInfo(file, relative, properties.Size, properties.DateModified));
                }
                catch
                {
                }
            }
        }
    }

    public static string FormatSize(double bytes)
    {
        return bytes switch
        {
            >= 1024d * 1024 * 1024 => $"{bytes / 1024 / 1024 / 1024:0.##} GB",
            >= 1024d * 1024 => $"{bytes / 1024 / 1024:0.#} MB",
            _ => $"{bytes / 1024:0} KB"
        };
    }

    public static async Task<StorageFolder> GetCacheFolderAsync()
    {
        try
        {
            return await StorageFolder.GetFolderFromPathAsync(AppDepository.Resolve<PlaybackSettings>().CacheDirectory);
        }
        catch
        {
            return ApplicationData.Current.LocalCacheFolder;
        }
    }

    public static async Task<ulong> GetCacheUsageAsync()
    {
        var root = await GetCacheFolderAsync().ConfigureAwait(false);
        ulong total = 0;
        foreach (var item in await CollectCacheAsync(root).ConfigureAwait(false)) total += item.Size;
        return total;
    }

    public static void ScheduleTrim(TimeSpan? delay = null)
    {
        if (Interlocked.Exchange(ref _trimScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var wait = delay ?? TimeSpan.FromSeconds(5);
                var sinceLast = DateTime.UtcNow - _lastTrim;
                if (sinceLast < TimeSpan.FromMinutes(1)) wait = TimeSpan.FromMinutes(1) - sinceLast;
                await Task.Delay(wait).ConfigureAwait(false);
                Volatile.Write(ref _trimScheduled, 0);
                await TrimCacheAsync().ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref _trimScheduled, 0);
            }
        });
    }

    public static async Task TrimCacheAsync()
    {
        if (!await _trimLock.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            _lastTrim = DateTime.UtcNow;
            var limit = (ulong)(AppDepository.Resolve<PlaybackSettings>().CacheLimitGB * 1024 * 1024 * 1024);
            if (limit == 0) return;
            var root = await GetCacheFolderAsync().ConfigureAwait(false);
            var items = await CollectCacheAsync(root).ConfigureAwait(false);
            ulong total = 0;
            foreach (var item in items) total += item.Size;
            if (total <= limit) return;
            var target = limit / 10 * 9;
            foreach (var item in items.OrderBy(i => i.Modified))
            {
                if (total <= target) break;
                try
                {
                    await item.File.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    total -= item.Size;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        finally
        {
            _trimLock.Release();
        }
    }
}
