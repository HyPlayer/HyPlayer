#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
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

    public static async Task<T?> GetOrCreateCacheAsync<T>(string type, string id, Func<Task<T?>> creator, TimeSpan? expiration = null, bool forceRefresh = false, bool forceUseCache = false) where T : class
    {
        if (!Common.Setting.enableApiCache)
        {
            return await creator();
        }

        if (cacheFolder == null)
        {
            await InitializeAsync();
        }
        
        // create new type dir
        var dir = await cacheFolder!.CreateFolderAsync(type, CreationCollisionOption.OpenIfExists);
        restart:
        var fileName = $"{id}.cache";
        bool hasCache = false;
        if (await dir.TryGetItemAsync(fileName) is StorageFile cacheFile && !forceRefresh)
        {
            hasCache = true;
            // Check for expiration
            var properties = await cacheFile.GetBasicPropertiesAsync();
            if (forceUseCache || !expiration.HasValue || DateTimeOffset.Now - properties.DateModified < expiration.Value)
            {
                // Cache is still valid, read from it
                using var stream = await cacheFile.OpenStreamForReadAsync();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                try
                {
                    var rst = JsonConvert.DeserializeObject<T>(content);
                    return rst;
                }
                catch (Exception e)
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
            var json = JsonConvert.SerializeObject(data);
            var file = await dir.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            await FileIO.WriteTextAsync(file, json);
        }
        catch (Exception e)
        {
            //ignore
        }
        
        
        
        
        return data;
    }
    
    public static async Task ResetCacheAsync(string type, string id)
    {
        if (cacheFolder == null)
        {
            throw new InvalidOperationException("Cache folder is not initialized. Call InitializeAsync first.");
        }

        var dir = await cacheFolder.CreateFolderAsync(type, CreationCollisionOption.OpenIfExists);

        var fileName = $"{id}.cache";

        if (await dir.TryGetItemAsync(fileName) is StorageFile file)
        {
            await file.DeleteAsync();
        }
    }
    
    public static async Task ClearCacheAsync()
    {
        if (cacheFolder == null)
        {
            throw new InvalidOperationException("Cache folder is not initialized. Call InitializeAsync first.");
        }
        
        var files = await cacheFolder.GetFilesAsync();
        foreach (var file in files)
        {
            await file.DeleteAsync();
        }
    }
}