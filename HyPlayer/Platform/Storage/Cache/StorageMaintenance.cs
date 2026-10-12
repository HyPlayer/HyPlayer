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

public enum StorageContentKind
{
    Music,
    Cache
}

public sealed record StorageItemInfo(StorageFile File, string RelativeFolder, ulong Size, DateTimeOffset Modified);

public sealed record StorageMigrationResult(int Moved, int Skipped, int Failed, int Remaining, int OriginalsKept);

public static class StorageMaintenance
{
    private const int MaxMusicDepth = 4;
    private const string StagedSuffix = ".hymove";

    private static readonly string[] _musicExtensions =
    [
        ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav", ".ogg", ".opus", ".aac", ".lrc", ".yrc", ".ttml", ".alrc",
        ".lys"
    ];

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

    private static bool IsMusicFile(string name)
    {
        var extension = Path.GetExtension(name);
        return Array.FindIndex(_musicExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)) >= 0;
    }

    public static async Task<List<StorageItemInfo>> CollectCacheAsync(
        StorageFolder root,
        string? excludePath = null,
        CancellationToken ct = default)
    {
        var result = new List<StorageItemInfo>();
        await AddFilesAsync(result, root, "", IsCacheAudioFile, ct).ConfigureAwait(false);
        foreach (var folder in await root.GetFoldersAsync())
        {
            if (!IsCacheFolderName(folder.Name) || FolderAccess.IsSamePath(folder.Path, excludePath)) continue;
            await AddFilesAsync(result, folder, folder.Name, _ => true, ct).ConfigureAwait(false);
        }

        return result;
    }

    public static async Task<List<StorageItemInfo>> CollectAsync(
        StorageFolder root,
        StorageContentKind kind,
        string? excludePath = null,
        CancellationToken ct = default)
    {
        if (kind == StorageContentKind.Cache)
            return await CollectCacheAsync(root, excludePath, ct).ConfigureAwait(false);

        var result = new List<StorageItemInfo>();
        var cacheFolder = await GetCacheFolderAsync().ConfigureAwait(false);
        var sharesCacheRoot = FolderAccess.IsSamePath(root.Path, cacheFolder.Path);
        await WalkAsync(root, "", 0).ConfigureAwait(false);
        return result;

        async Task WalkAsync(StorageFolder folder, string relative, int depth)
        {
            var atCacheRoot = sharesCacheRoot && depth == 0;
            await AddFilesAsync(
                    result,
                    folder,
                    relative,
                    name => IsMusicFile(name) && !(atCacheRoot && IsCacheAudioFile(name)),
                    ct)
                .ConfigureAwait(false);
            if (depth >= MaxMusicDepth) return;
            foreach (var child in await folder.GetFoldersAsync())
            {
                if (FolderAccess.IsSamePath(child.Path, excludePath)
                    || (atCacheRoot && IsCacheFolderName(child.Name)))
                    continue;

                await WalkAsync(
                        child,
                        relative.Length == 0 ? child.Name : Path.Combine(relative, child.Name),
                        depth + 1)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task AddFilesAsync(
        List<StorageItemInfo> result,
        StorageFolder folder,
        string relative,
        Func<string, bool> filter,
        CancellationToken ct)
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

    public static async Task<StorageMigrationResult> MigrateAsync(
        StorageFolder from,
        StorageFolder to,
        IReadOnlyList<StorageItemInfo> items,
        bool deleteOriginal,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        int moved = 0, skipped = 0, failed = 0, originalsKept = 0, done = 0;
        var folders = new Dictionary<string, StorageFolder>(StringComparer.OrdinalIgnoreCase) { [""] = to };
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var destination = await GetFolderAsync(item.RelativeFolder).ConfigureAwait(false);
                if (await destination.TryGetItemAsync(item.File.Name) is not null)
                {
                    skipped++;
                }
                else
                {
                    await CopyVerifiedAsync(item.File, destination).ConfigureAwait(false);
                    moved++;
                    if (deleteOriginal && !await TryDeleteAsync(item.File).ConfigureAwait(false))
                        originalsKept++;
                    else if (deleteOriginal && item.RelativeFolder.Length > 0)
                        touched.Add(item.RelativeFolder);
                }
            }
            catch
            {
                failed++;
            }

            progress?.Report(++done);
        }

        foreach (var relative in touched.OrderByDescending(r => r.Length))
            try
            {
                var current = relative;
                while (current.Length > 0)
                {
                    if (await from.TryGetItemAsync(current) is not StorageFolder folder) break;
                    if ((await folder.GetItemsAsync(0, 1)).Count > 0) break;
                    await folder.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    current = Path.GetDirectoryName(current) ?? "";
                }
            }
            catch
            {
            }

        return new StorageMigrationResult(moved, skipped, failed, items.Count - done, originalsKept);

        async Task<StorageFolder> GetFolderAsync(string relative)
        {
            if (folders.TryGetValue(relative, out var cached)) return cached;
            var parent = await GetFolderAsync(Path.GetDirectoryName(relative) ?? "").ConfigureAwait(false);
            var created = await parent.CreateFolderAsync(
                Path.GetFileName(relative),
                CreationCollisionOption.OpenIfExists);
            folders[relative] = created;
            return created;
        }
    }

    private static async Task CopyVerifiedAsync(StorageFile source, StorageFolder destination)
    {
        var staged = await source.CopyAsync(
            destination,
            source.Name + StagedSuffix,
            NameCollisionOption.ReplaceExisting);
        try
        {
            var expected = (await source.GetBasicPropertiesAsync()).Size;
            if ((await staged.GetBasicPropertiesAsync()).Size != expected)
                throw new IOException();

            await staged.RenameAsync(source.Name, NameCollisionOption.FailIfExists);
        }
        catch
        {
            await TryDeleteAsync(staged).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> TryDeleteAsync(StorageFile file)
    {
        try
        {
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            return true;
        }
        catch
        {
            return false;
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
            var custom = AppDepository.Resolve<PlaybackSettings>().CustomCacheDirectory;
            if (await FolderAccess.OpenAsync(custom).ConfigureAwait(false) is { } folder) return folder;
        }
        catch
        {
        }

        return ApplicationData.Current.LocalCacheFolder;
    }

    public static async Task ClearAudioCacheAsync()
    {
        var root = await GetCacheFolderAsync().ConfigureAwait(false);
        foreach (var file in await root.GetFilesAsync())
            if (IsCacheAudioFile(file.Name))
                await TryDeleteAsync(file).ConfigureAwait(false);
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
