#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using Windows.Media.Core;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;

namespace HyPlayer.Services.Playback.MediaProviders;

/// <summary>
/// <c>nca</c> — 网易云在线播放 + 缓存策略提供者。
/// <para>
/// 优先检查本地缓存目录，命中则通过 <see cref="MediaSource.CreateFromStorageFile"/> 返回；
/// 未命中则通过 <see cref="BackgroundDownloader"/> 下载并使用
/// <see cref="MediaSource.CreateFromDownloadOperation"/> 实现边下边播。
/// </para>
/// </summary>
public sealed class CachedNeteaseProvider : IMediaSourceProvider
{
    private const string CacheFileNameFormat = "{0}.{1}";
    private const string SongUrlCacheKeyFormat = "{0}_{1}";
    private const int SongUrlCacheMinutes = 20;

    private readonly Setting _setting;
    private readonly HttpClient _httpClient;
    private readonly NeteaseCloudMusicApiHandler _neteaseApi;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly BackgroundDownloader _downloader = new();

    /// <summary>
    /// 正在进行的下载操作，防止重复下载
    /// </summary>
    private readonly ConcurrentDictionary<string, DownloadOperation> _downloadOperations = new();

    /// <inheritdoc />
    public string Id => "nca";

    /// <summary>
    /// 创建 <see cref="CachedNeteaseProvider"/> 实例
    /// </summary>
    /// <param name="setting">应用设置，用于获取缓存目录和音质配置</param>
    /// <param name="httpClient">HTTP 客户端，用于预检请求</param>
    /// <param name="neteaseApi">网易云 API 处理器，用于获取播放链接</param>
    public CachedNeteaseProvider(
        Setting setting,
        HttpClient httpClient,
        NeteaseCloudMusicApiHandler neteaseApi,
        IBackgroundTaskRunner taskRunner)
    {
        _setting = setting;
        _httpClient = httpClient;
        _neteaseApi = neteaseApi;
        _taskRunner = taskRunner;
    }

    /// <inheritdoc />
    public async Task<MediaSource?> CreateAsync(HyPlayItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var (playUrl, _) = await GetPlayUrlAsync(item, ct);

        // 先检查缓存
        var cacheFile = await GetCacheFileAsync(item, ct);
        if (cacheFile != null)
        {
            return MediaSource.CreateFromStorageFile(cacheFile);
        }

        if (playUrl == null)
            return null;

        // 缓存未命中，使用 Polly 快速失败策略下载
        var result = await RetryPolicies.FastFailPolicy.ExecuteAndCaptureAsync(
            async () => await DownloadAndCreateMediaSourceAsync(item, playUrl, ct));

        return result.Result;
    }

    /// <summary>
    /// 获取歌曲播放链接，支持 SimpleCacher 缓存（20 分钟 TTL）
    /// </summary>
    private async Task<(string? Url, long Size)> GetPlayUrlAsync(HyPlayItem item, CancellationToken ct)
    {
        var playUrl = item.Url;
        var size = item.Size;

        if ((string.IsNullOrEmpty(item.Url) || _setting.songUrlLazyGet) && item.Id != "-1")
        {
            var songResult = await RetryPolicies.UrlFetchPolicy.ExecuteAsync(async () =>
            {
                var result = await SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.SongUrl,
                    string.Format(SongUrlCacheKeyFormat, item.Id, _setting.audioRate),
                    async () =>
                    {
                        ct.ThrowIfCancellationRequested();

                        var songRequest = new SongUrlRequest
                        {
                            Level = _setting.audioRate,
                            Id = item.Id
                        };

                        var songRes = await _neteaseApi.RequestAsync(
                            NeteaseApis.SongUrlApi, songRequest);

                        if (songRes.IsError && songRes.Error != null)
                        {
                            throw songRes.Error;
                        }

                        return songRes.Value;
                    },
                    TimeSpan.FromMinutes(SongUrlCacheMinutes),
                    cancellationToken: ct);

                return result ?? throw new InvalidOperationException("下载链接获取失败");
            });

            if (songResult?.SongUrls?[0].Code == 200)
            {
                if (songResult.SongUrls[0].FreeTrialInfo is not null && _setting.jumpVipSongPlaying)
                {
                    return (null, 0);
                }

                var songUrl = songResult.SongUrls[0];
                playUrl = songUrl.Url;
                size = songUrl.Size;
                item.Size = size;
                item.Bitrate = Convert.ToInt32(songUrl.BitRate);
                item.SubExt = songUrl.Type?.ToLowerInvariant() ?? string.Empty;
                item.QualityTag = item.GetQualityTagText(_setting.audioRate);
                if (_setting.UseHttpWhenGettingSongs && (playUrl?.Contains("https://") ?? false))
                {
                    playUrl = playUrl.Replace("https://", "http://");
                }

                if (playUrl != null) item.Url = playUrl;
            }
            else
            {
                throw new InvalidOperationException("下载链接获取失败");
            }
        }

        return (playUrl, size);
    }

    /// <summary>
    /// 检查缓存文件是否存在且大小匹配
    /// </summary>
    private async Task<StorageFile?> GetCacheFileAsync(HyPlayItem item, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var cacheFolder = await StorageFolder.GetFolderFromPathAsync(_setting.cacheDir);
            var fileName = string.Format(CacheFileNameFormat, item.Id, item.SubExt);
            var cacheFile = await cacheFolder.GetFileAsync(fileName);

            var properties = await cacheFile.GetBasicPropertiesAsync();
            if (properties.Size == (ulong)(item.Size))
            {
                return cacheFile;
            }
            else
            {
                await cacheFile.DeleteAsync();
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 下载歌曲并创建媒体源（边下边播）
    /// </summary>
    private async Task<MediaSource?> DownloadAndCreateMediaSourceAsync(
        HyPlayItem item, string playUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(playUrl))
            throw new InvalidOperationException("Play URL is null");

        // 检查是否已存在下载操作
        if (_downloadOperations.TryGetValue(item.Id, out var existingOperation))
        {
            return MediaSource.CreateFromDownloadOperation(existingOperation);
        }

        using var message = new HttpRequestMessage(HttpMethod.Head, playUrl);
        using var preflightResponse = await _httpClient.SendAsync(message, ct);
        var modified = preflightResponse.Content.Headers.LastModified;
        var headerIsValid = modified is not null;

        var destinationFolder = await StorageFolder.GetFolderFromPathAsync(_setting.cacheDir);
        var fileName = string.Format(CacheFileNameFormat, item.Id, item.SubExt);
        var destinationFile = await destinationFolder.CreateFileAsync(
            fileName, CreationCollisionOption.ReplaceExisting);

        var operation = _downloader.CreateDownload(new Uri(playUrl), destinationFile);
        operation.IsRandomAccessRequired = headerIsValid;
        _downloadOperations[item.Id] = operation;

        _taskRunner.Forget(async () =>
        {
            try
            {
                await operation.StartAsync().AsTask(ct);
            }
            finally
            {
                _downloadOperations.TryRemove(item.Id, out _);
            }
        }, "download and cache NetEase media");

        return headerIsValid
            ? MediaSource.CreateFromDownloadOperation(operation)
            : MediaSource.CreateFromUri(new Uri(playUrl));
    }
}
