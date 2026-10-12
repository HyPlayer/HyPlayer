#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Serialization;

namespace HyPlayer.Platform.Storage.Cache;

public static class SimpleCacher
{
    private static StorageFolder? _cacheFolder;
    private static int _folderVersion;
    private static readonly object _folderSync = new();
    private static readonly SemaphoreSlim _initializeLock = new(1, 1);
    private static readonly ConcurrentDictionary<Type, bool> _jsonSupportedTypes = new();


    public static async Task InitializeAsync()
    {
        await GetFolderAsync().ConfigureAwait(false);
        // cacheFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("cache", CreationCollisionOption.OpenIfExists);
    }

    public static void Reset()
    {
        lock (_folderSync)
        {
            _folderVersion++;
            _cacheFolder = null;
        }
    }

    private static async Task<StorageFolder> GetFolderAsync()
    {
        if (_cacheFolder is { } current) return current;

        await _initializeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            int version;
            lock (_folderSync)
            {
                if (_cacheFolder is { } initialized) return initialized;
                version = _folderVersion;
            }

            var folder = await StorageMaintenance.GetCacheFolderAsync().ConfigureAwait(false);
            lock (_folderSync)
            {
                if (version == _folderVersion) _cacheFolder = folder;
            }

            return folder;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public static async Task<T?> GetOrCreateCacheAsync<T>(CacheType cacheType, string id, Func<Task<T?>> creator,
        TimeSpan? expiration = null, bool forceRefresh = false, bool forceUseCache = false,
        CancellationToken cancellationToken = default) where T : class
    {
        if (!Ioc.Default.GetRequiredService<ApiSettings>().EnableApiCache) return await creator();

        var type = cacheType.ToString();

        // create new type dir
        StorageFolder dir;
        try
        {
            dir = await (await GetFolderAsync()).CreateFolderAsync(type, CreationCollisionOption.OpenIfExists);
        }
        catch
        {
            Reset();
            return await creator();
        }

        cancellationToken.ThrowIfCancellationRequested();
        restart:
        var fileName = $"{id}.cache";
        var hasCache = false;
        var supportsJsonCache = SupportsJsonCache<T>();
        if (supportsJsonCache && await dir.TryGetItemAsync(fileName) is StorageFile cacheFile && !forceRefresh)
        {
            hasCache = true;
            // Check for expiration
            var properties = await cacheFile.GetBasicPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (forceUseCache || !expiration.HasValue ||
                DateTimeOffset.Now - properties.DateModified < expiration.Value)
            {
                // Cache is still valid, read from it
                try
                {
                    using var stream = await cacheFile.OpenStreamForReadAsync();
                    var rst = await JsonSerializer.DeserializeAsync<T>(
                        stream,
                        JsonDefaults.Options,
                        cancellationToken).ConfigureAwait(false);
                    return rst;
                }
                catch (NotSupportedException)
                {
                    _jsonSupportedTypes[typeof(T)] = false;
                    supportsJsonCache = false;
                    if (forceUseCache)
                        return default;
                }
                catch
                {
                    if (forceUseCache)
                        return default;
                }
            }
        }

        // Cache is either not found or expired, create a new one
        T? data = default;
        try
        {
            data = await creator();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            if (hasCache)
            {
                // If we had a cache but the creator failed, use the existing cache
                forceUseCache = true;
                goto restart;
            }
        }

        if (data == null) return default;

        if (!supportsJsonCache) return data;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await dir.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            await JsonSerializer.SerializeAsync(stream, data, JsonDefaults.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            _jsonSupportedTypes[typeof(T)] = false;
        }
        catch
        {
            //ignore
        }


        return data;
    }

    public static async Task<T?> GetOrCreateCacheOrThrowAsync<T>(CacheType cacheType, string id,
        Func<Task<T?>> creator, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        where T : class
    {
        ExceptionDispatchInfo? failure = null;
        var result = await GetOrCreateCacheAsync(cacheType, id, async () =>
        {
            try
            {
                return await creator();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }, expiration, cancellationToken: cancellationToken);
        if (result is null)
            failure?.Throw();

        return result;
    }

    private static bool SupportsJsonCache<T>() where T : class
    {
        return _jsonSupportedTypes.GetOrAdd(typeof(T), static type =>
        {
            try
            {
                return JsonDefaults.Options.TypeInfoResolver?.GetTypeInfo(type, JsonDefaults.Options) is not null;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        });
    }

    public static async Task ResetCacheAsync(CacheType type, string id, bool isPrefix = false)
    {
        var root = await GetFolderAsync();
        var dir = await root.CreateFolderAsync(type.ToString()!, CreationCollisionOption.OpenIfExists);
        var files = await dir.GetFilesAsync();
        foreach (var file in files)
            if (isPrefix && file.Name.StartsWith(id))
                await file.DeleteAsync();
            else if (!isPrefix && file.Name == $"{id}.cache") await file.DeleteAsync();
    }

    public static async Task ClearCacheAsync(CacheType type)
    {
        var root = await GetFolderAsync();
        var dir = await root.CreateFolderAsync(type.ToString()!, CreationCollisionOption.OpenIfExists);
        var files = await dir.GetFilesAsync();
        foreach (var file in files) await file.DeleteAsync();
    }

    public static async Task ClearAllCacheAsync()
    {
        var root = await GetFolderAsync();
        var files = await root.GetFoldersAsync();
        foreach (var file in files)
            if (StorageMaintenance.IsCacheFolderName(file.Name))
                await file.DeleteAsync();
    }
}

public enum CacheType
{
    Unspecified,
    Comments,
    SongUrl,
    HyLyricInfo,
    LyricApi,
    SongDetail,
    AlbumInfo,
    PlaylistTracks,
    PlaylistDetail,
    PlaylistTracksDetail,
    AlbumDynamic,
    ArtistDetail,
    ArtistSongsDetial,
    ArtistTopSongsDetail,
    ArtistAlbumsList,
    Login,
    Toplist,
    UserDetail,
    UserPlaylist,
    RadioPrograms,
    RadioInfo
}
