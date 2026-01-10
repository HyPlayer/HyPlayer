#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Classes;

public static class SimpleCacher
{
    private static StorageFolder? cacheFolder;


    public static async Task InitializeAsync()
    {
        cacheFolder ??= await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir);
        // cacheFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("cache", CreationCollisionOption.OpenIfExists);
    }

    public static async Task<T?> GetOrCreateCacheAsync<T>(CacheType cacheType, string id, Func<Task<T?>> creator, TimeSpan? expiration = null, bool forceRefresh = false, bool forceUseCache = false, CancellationToken cancellationToken = default) where T : class
    {
        if (!Common.Setting.enableApiCache)
        {
            return await creator();
        }

        if (cacheFolder == null)
        {
            await InitializeAsync();
        }
        var type = cacheType.ToString();

        // create new type dir
        var dir = await cacheFolder!.CreateFolderAsync(type, CreationCollisionOption.OpenIfExists);
        cancellationToken.ThrowIfCancellationRequested();
    restart:
        var fileName = $"{id}.cache";
        bool hasCache = false;
        if (await dir.TryGetItemAsync(fileName) is StorageFile cacheFile && !forceRefresh)
        {
            hasCache = true;
            // Check for expiration
            var properties = await cacheFile.GetBasicPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (forceUseCache || !expiration.HasValue || DateTimeOffset.Now - properties.DateModified < expiration.Value)
            {
                // Cache is still valid, read from it
                using var stream = await cacheFile.OpenStreamForReadAsync();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                if (content.Length == 0)
                {
                    return default;
                }
                try
                {
                    var rst = JsonSerializer.Deserialize<T>(content);
                    return rst;
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
        if (data == null)
        {
            return default;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.Serialize(data);
            var file = await dir.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            await FileIO.WriteTextAsync(file, json);
        }
        catch
        {
            //ignore
        }




        return data;
    }

    public static async Task ResetCacheAsync(CacheType type, string id, bool isPrefix = false)
    {
        if (cacheFolder == null)
        {
            throw new InvalidOperationException("Cache folder is not initialized. Call InitializeAsync first.");
        }

        var dir = await cacheFolder.CreateFolderAsync(type.ToString()!, CreationCollisionOption.OpenIfExists);
        var files = await dir.GetFilesAsync();
        foreach (var file in files)
        {
            if (isPrefix && file.Name.StartsWith(id))
            {
                await file.DeleteAsync();
            }
            else if (!isPrefix && file.Name == $"{id}.cache")
            {
                await file.DeleteAsync();
            }
        }
    }

    public static async Task ClearCacheAsync(CacheType type)
    {
        if (cacheFolder == null)
        {
            throw new InvalidOperationException("Cache folder is not initialized. Call InitializeAsync first.");
        }

        var dir = await cacheFolder.CreateFolderAsync(type.ToString()!, CreationCollisionOption.OpenIfExists);
        var files = await dir.GetFilesAsync();
        foreach (var file in files)
        {
            await file.DeleteAsync();
        }
    }

    public static async Task ClearAllCacheAsync()
    {
        if (cacheFolder == null)
        {
            throw new InvalidOperationException("Cache folder is not initialized. Call InitializeAsync first.");
        }

        var files = await cacheFolder.GetFoldersAsync();
        foreach (var file in files)
        {
            await file.DeleteAsync();
        }
    }
}

public enum CacheType
{
    Unspecified,
    Comments,
    SongUrl,
    LyricInfo,
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
    RadioInfo,

}