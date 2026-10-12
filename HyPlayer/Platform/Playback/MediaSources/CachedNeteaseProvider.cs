using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Network;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class CachedNeteaseProvider : IMediaSourceProvider, IMediaPrefetchProvider, IMediaTransferProvider,
    IMediaOfflineProvider
{
    public const string ProviderId = "nca";
    private const string CacheFileNameFormat = "{0}.{1}";

    private static readonly ConcurrentDictionary<string, bool> _randomAccessSupportByHost =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] _offlineExtensions = ["flac", "mp3", "m4a", "ogg"];

    private readonly BackgroundDownloader _downloader = new();
    private readonly ConcurrentDictionary<string, DownloadOperation> _downloadOperations = new();
    private readonly HttpClient _httpClient;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _transferCancellations = new();

    public CachedNeteaseProvider(
        HttpClient httpClient,
        IBackgroundTaskRunner taskRunner)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
    }

    public string Id => ProviderId;

    public async Task<MediaSource?> CreateAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var playUri = resource.Uri;
        if (_downloadOperations.TryGetValue(song.ActualId, out var pendingOperation))
            return pendingOperation.IsRandomAccessRequired || playUri is null
                ? await CreateFromDownloadAsync(resource, pendingOperation)
                : MediaSource.CreateFromUri(playUri);

        var cacheFile = await GetCacheFileAsync(song, resource, ct);
        if (cacheFile is not null)
            return await MediaSourceFiles.CreateAsync(cacheFile, GetExtension(resource));

        if (playUri is null)
            return null;

        var result = await RetryPolicies.FastFailPolicy.ExecuteAndCaptureAsync(
            () => DownloadAndCreateMediaSourceAsync(song, resource, playUri, ct));
        ct.ThrowIfCancellationRequested();
        return result.Result ?? MediaSource.CreateFromUri(playUri);
    }

    public async Task PrefetchAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct)
    {
        if (resource.Uri is not { } playUri || _downloadOperations.ContainsKey(song.ActualId))
            return;
        if (await GetCacheFileAsync(song, resource, ct) is not null)
            return;

        var supportsRandomAccess = await SupportsRandomAccessAsync(playUri, ct);
        var destinationFile = await CreateCacheFileAsync(song, resource);
        var operation = _downloader.CreateDownload(playUri, destinationFile);
        operation.IsRandomAccessRequired = supportsRandomAccess;
        if (!_downloadOperations.TryAdd(song.ActualId, operation))
            return;

        StartTransfer(song.ActualId, operation);
    }

    public bool IsTransferring(SingleSongBase song)
    {
        return _downloadOperations.ContainsKey(song.ActualId);
    }

    public void CancelTransfersExcept(SingleSongBase keep)
    {
        foreach (var pair in _transferCancellations)
        {
            if (pair.Key == keep.ActualId)
                continue;

            try
            {
                pair.Value.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task<bool> IsCachedAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct)
    {
        return !_downloadOperations.ContainsKey(song.ActualId)
               && await GetCacheFileAsync(song, resource, ct) is not null;
    }

    public Task<StorageFile?> FindCachedFileAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct)
    {
        return GetCacheFileAsync(song, resource, ct);
    }

    public async Task<StorageFile?> FindOfflineFileAsync(SingleSongBase song)
    {
        try
        {
            if (_downloadOperations.ContainsKey(song.ActualId))
                return null;

            var cacheFolder = await StorageMaintenance.GetCacheFolderAsync();
            foreach (var extension in _offlineExtensions)
            {
                var fileName = string.Format(CacheFileNameFormat, song.ActualId, extension);
                if (await cacheFolder.TryGetItemAsync(fileName) is not StorageFile file)
                    continue;
                if ((await file.GetBasicPropertiesAsync()).Size == 0)
                    continue;

                return file;
            }
        }
        catch
        {
        }

        return null;
    }

    private void StartTransfer(string id, DownloadOperation operation)
    {
        var cancellation = new CancellationTokenSource();
        _transferCancellations[id] = cancellation;
        _taskRunner.Forget(
            RunTransferAsync(id, operation, cancellation),
            "download and cache NetEase media");
    }

    private async Task RunTransferAsync(string id, DownloadOperation operation, CancellationTokenSource cancellation)
    {
        try
        {
            await operation.StartAsync().AsTask(cancellation.Token);
        }
        finally
        {
            _downloadOperations.TryRemove(new KeyValuePair<string, DownloadOperation>(id, operation));
            _transferCancellations.TryRemove(new KeyValuePair<string, CancellationTokenSource>(id, cancellation));
            cancellation.Dispose();
            StorageMaintenance.ScheduleTrim();
        }
    }

    private static string GetExtension(MusicResourceBase resource)
    {
        return string.IsNullOrWhiteSpace(resource.ExtensionName)
            ? "mp3"
            : resource.ExtensionName.TrimStart('.').ToLowerInvariant();
    }

    private static async Task<MediaSource> CreateFromDownloadAsync(
        MusicResourceBase resource,
        DownloadOperation operation)
    {
        var source = GetExtension(resource) == "flac"
            ? await RechunkedAudioSource.FromDownloadAsync(operation)
            : MediaSource.CreateFromDownloadOperation(operation);
        source.CustomProperties[MediaSourceTraits.Progressive] = true;
        return source;
    }

    private async Task<StorageFile> CreateCacheFileAsync(SingleSongBase song, MusicResourceBase resource)
    {
        var cacheFolder = await StorageMaintenance.GetCacheFolderAsync();
        var fileName = string.Format(CacheFileNameFormat, song.ActualId, GetExtension(resource));
        return await cacheFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
    }

    private async Task<StorageFile?> GetCacheFileAsync(
        SingleSongBase song,
        MusicResourceBase resource,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_downloadOperations.ContainsKey(song.ActualId))
                return null;

            var cacheFolder = await StorageMaintenance.GetCacheFolderAsync();
            var fileName = string.Format(CacheFileNameFormat, song.ActualId, GetExtension(resource));
            if (await cacheFolder.TryGetItemAsync(fileName) is not StorageFile cacheFile)
                return null;

            var properties = await cacheFile.GetBasicPropertiesAsync();
            var expectedSize = resource is NeteaseMusicResource neteaseResource ? neteaseResource.Size : 0;
            if (expectedSize > 0 && properties.Size == (ulong)expectedSize)
                return cacheFile;

            await cacheFile.DeleteAsync();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> SupportsRandomAccessAsync(Uri playUri, CancellationToken ct)
    {
        if (_randomAccessSupportByHost.TryGetValue(playUri.Host, out var cached))
            return cached;

        using var message = new HttpRequestMessage(HttpMethod.Head, playUri);
        using var preflightResponse = await _httpClient.SendAsync(message, ct);
        var supported = preflightResponse.Content.Headers.LastModified is not null;
        if (preflightResponse.IsSuccessStatusCode)
            _randomAccessSupportByHost[playUri.Host] = supported;
        return supported;
    }

    private async Task<MediaSource?> DownloadAndCreateMediaSourceAsync(
        SingleSongBase song,
        MusicResourceBase resource,
        Uri playUri,
        CancellationToken ct)
    {
        if (_downloadOperations.TryGetValue(song.ActualId, out var existingOperation))
            return await CreateFromDownloadAsync(resource, existingOperation);

        var supportsRandomAccess = await SupportsRandomAccessAsync(playUri, ct);
        var destinationFile = await CreateCacheFileAsync(song, resource);
        var operation = _downloader.CreateDownload(playUri, destinationFile);
        operation.IsRandomAccessRequired = supportsRandomAccess;
        _downloadOperations[song.ActualId] = operation;
        StartTransfer(song.ActualId, operation);

        return supportsRandomAccess
            ? await CreateFromDownloadAsync(resource, operation)
            : MediaSource.CreateFromUri(playUri);
    }
}
