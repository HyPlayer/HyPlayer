using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Threading;
using HyPlayer.Domain;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyPlayer.Features.Playback.DynamicCover;

public sealed record DynamicCoverVideo(StorageFile File, string? PreviewImageUrl);

public interface IDynamicCoverService
{
    AppleMusicCoverStatus AppleMusicStatus { get; }
    Task<DynamicCoverVideo?> ResolveAsync(SingleSongBase song, DynamicCoverSource source, CancellationToken ct);
    Task<InMemoryRandomAccessStream?> CreatePosterAsync(DynamicCoverVideo video, CancellationToken ct);
    Task CheckAppleMusicAsync(CancellationToken ct = default);
    Task ClearCacheAsync();
}

internal sealed class DynamicCoverService : IDynamicCoverService
{
    private const int MaxCachedVideos = 20;
    private const int MaxVideoEdge = 1080;
    private static readonly TimeSpan AppleMusicRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly (string Storefront, string Language)[] Storefronts = [("cn", "zh-Hans-CN"), ("us", "en-US")];

    private readonly IDynamicCoverProvidable _netease;
    private readonly IUIThreadDispatcher _ui;
    private readonly HttpClient _http;
    private readonly AppleMusicCatalog _appleMusic;
    private readonly object _sync = new();
    private readonly Dictionary<string, DynamicCoverVideo?> _resolved = new();
    private readonly LinkedList<string> _videoOrder = new();
    private readonly SemaphoreSlim _folderGate = new(1, 1);
    private long _appleMusicFailedAtTicks;
    private int _cacheVersion;

    public DynamicCoverService(IDynamicCoverProvidable netease, IUIThreadDispatcher ui)
    {
        _netease = netease;
        _ui = ui;
        _http = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _appleMusic = new AppleMusicCatalog(_http);
    }

    public AppleMusicCoverStatus AppleMusicStatus { get; } = new();

    public async Task<DynamicCoverVideo?> ResolveAsync(SingleSongBase song, DynamicCoverSource source, CancellationToken ct)
    {
        if (song.ProviderId != "ncm" || string.IsNullOrEmpty(song.ActualId)) return null;
        DynamicCoverSource[] order = source switch
        {
            DynamicCoverSource.AppleMusic => [DynamicCoverSource.AppleMusic],
            DynamicCoverSource.Netease => [DynamicCoverSource.Netease],
            _ => [DynamicCoverSource.AppleMusic, DynamicCoverSource.Netease]
        };
        foreach (var candidate in order)
        {
            var key = (candidate == DynamicCoverSource.AppleMusic ? "am:" : "ncm:") + song.ActualId;
            lock (_sync)
            {
                if (_resolved.TryGetValue(key, out var known))
                {
                    if (known is not null) return known;
                    continue;
                }
            }

            var version = Volatile.Read(ref _cacheVersion);
            var (found, video) = candidate == DynamicCoverSource.AppleMusic
                ? await ResolveAppleMusicAsync(song, ct).ConfigureAwait(false)
                : await ResolveNeteaseAsync(song, ct).ConfigureAwait(false);
            if (!found) continue;
            foreach (var evicted in Remember(key, video, version))
                await TryDeleteAsync(evicted).ConfigureAwait(false);
            if (video is not null) return video;
        }

        return null;
    }

    public async Task<InMemoryRandomAccessStream?> CreatePosterAsync(DynamicCoverVideo video, CancellationToken ct)
    {
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(video.File).AsTask(ct).ConfigureAwait(false);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            using var frame = await composition
                .GetThumbnailAsync(TimeSpan.Zero, 0, 0, VideoFramePrecision.NearestFrame)
                .AsTask(ct)
                .ConfigureAwait(false);
            var poster = new InMemoryRandomAccessStream();
            try
            {
                await RandomAccessStream.CopyAsync(frame, poster).AsTask(ct).ConfigureAwait(false);
                poster.Seek(0);
                return poster;
            }
            catch
            {
                poster.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Dynamic cover first frame failed: {ex}");
        }

        if (string.IsNullOrEmpty(video.PreviewImageUrl)) return null;
        try
        {
            var bytes = await _http.GetByteArrayAsync(video.PreviewImageUrl, ct).ConfigureAwait(false);
            var poster = new InMemoryRandomAccessStream();
            await poster.WriteAsync(bytes.AsBuffer()).AsTask(ct).ConfigureAwait(false);
            poster.Seek(0);
            return poster;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dynamic cover preview download failed: {ex}");
            return null;
        }
    }

    public async Task CheckAppleMusicAsync(CancellationToken ct = default)
    {
        try
        {
            await _appleMusic.EnsureTokenAsync(ct).ConfigureAwait(false);
            await _appleMusic.SearchSongsAsync("cn", "zh-Hans-CN", "love", ct).ConfigureAwait(false);
            await MarkAppleMusicAsync(null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await MarkAppleMusicAsync(ReasonOf(ex)).ConfigureAwait(false);
        }
    }

    public async Task ClearCacheAsync()
    {
        lock (_sync)
        {
            _cacheVersion++;
            _resolved.Clear();
            _videoOrder.Clear();
        }

        await _folderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var folder = await GetFolderAsync().ConfigureAwait(false);
            foreach (var file in await folder.GetFilesAsync())
                await TryDeleteAsync(file).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dynamic cover cache cleanup failed: {ex}");
        }
        finally
        {
            _folderGate.Release();
        }
    }

    private async Task<(bool Found, DynamicCoverVideo? Video)> ResolveAppleMusicAsync(SingleSongBase song, CancellationToken ct)
    {
        if (!AppleMusicStatus.IsAvailable &&
            DateTimeOffset.UtcNow.UtcTicks - Volatile.Read(ref _appleMusicFailedAtTicks) < AppleMusicRetryDelay.Ticks)
            return (false, null);
        var query = new DynamicCoverQuery(
            song.Name,
            song.CreatorList ?? [],
            song.Album?.Name ?? string.Empty,
            song.Duration);
        var term = $"{song.Name} {query.Artists.FirstOrDefault()}".Trim();
        try
        {
            foreach (var (storefront, language) in Storefronts)
            {
                var songs = await _appleMusic.SearchSongsAsync(storefront, language, term, ct).ConfigureAwait(false);
                var matched = AppleMusicMatcher.PickSong(query, songs);
                if (matched?.Isrc is not { Length: > 0 } isrc) continue;
                var albums = await _appleMusic.GetAlbumsByIsrcAsync(storefront, language, isrc, ct).ConfigureAwait(false);
                var album = AppleMusicMatcher.PickAlbum(query, albums, matched.AlbumName);
                if (album?.VideoUrl is not { } videoUrl) continue;
                var file = await DownloadHlsAsync(new Uri(videoUrl), $"am_{album.Id}.mp4", ct).ConfigureAwait(false);
                await MarkAppleMusicAsync(null).ConfigureAwait(false);
                if (file is null) return (false, null);
                return (true, new DynamicCoverVideo(file, PreviewUrl(album.PreviewUrl)));
            }

            await MarkAppleMusicAsync(null).ConfigureAwait(false);
            return (true, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await MarkAppleMusicAsync(ReasonOf(ex)).ConfigureAwait(false);
            return (false, null);
        }
    }

    private async Task<(bool Found, DynamicCoverVideo? Video)> ResolveNeteaseAsync(SingleSongBase song, CancellationToken ct)
    {
        Uri? url;
        try
        {
            url = await _netease.GetDynamicCoverAsync(song.ActualId!, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NetEase dynamic cover request failed: {ex}");
            return (false, null);
        }

        if (url is null) return (true, null);
        try
        {
            var file = await DownloadFilesAsync([url], $"ncm_{song.ActualId}.mp4", ct).ConfigureAwait(false);
            return file is null ? (false, null) : (true, new DynamicCoverVideo(file, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"NetEase dynamic cover download failed: {ex}");
            return (false, null);
        }
    }

    private async Task<StorageFile?> DownloadHlsAsync(Uri master, string fileName, CancellationToken ct)
    {
        var variants = HlsPlaylist.ParseVariants(await _http.GetStringAsync(master, ct).ConfigureAwait(false), master);
        var variant = HlsPlaylist.SelectVariant(variants, MaxVideoEdge);
        if (variant is null) return null;
        var files = HlsPlaylist.ParseMediaFiles(
            await _http.GetStringAsync(variant.Uri, ct).ConfigureAwait(false),
            variant.Uri);
        return files.Count == 0 ? null : await DownloadFilesAsync(files, fileName, ct).ConfigureAwait(false);
    }

    private async Task<StorageFile?> DownloadFilesAsync(IReadOnlyList<Uri> parts, string fileName, CancellationToken ct)
    {
        await _folderGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var folder = await GetFolderAsync().ConfigureAwait(false);
            if (await folder.TryGetItemAsync(fileName) is StorageFile existing) return existing;
            var temp = await folder.CreateFileAsync(fileName + ".part", CreationCollisionOption.ReplaceExisting);
            try
            {
                await using (var output = await temp.OpenStreamForWriteAsync().ConfigureAwait(false))
                {
                    foreach (var part in parts)
                    {
                        using var response = await _http
                            .GetAsync(part, HttpCompletionOption.ResponseHeadersRead, ct)
                            .ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                        await input.CopyToAsync(output, ct).ConfigureAwait(false);
                    }
                }

                await temp.RenameAsync(fileName, NameCollisionOption.ReplaceExisting);
                return temp;
            }
            catch
            {
                await TryDeleteAsync(temp).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _folderGate.Release();
        }
    }

    private List<StorageFile> Remember(string key, DynamicCoverVideo? video, int version)
    {
        List<StorageFile> evicted = [];
        lock (_sync)
        {
            if (version != _cacheVersion) return evicted;
            _resolved[key] = video;
            if (video is null) return evicted;
            _videoOrder.Remove(key);
            _videoOrder.AddLast(key);
            while (_videoOrder.Count > MaxCachedVideos && _videoOrder.First is { } oldest)
            {
                _videoOrder.RemoveFirst();
                if (_resolved.Remove(oldest.Value, out var stale) && stale is not null &&
                    _resolved.Values.All(other => other?.File.Path != stale.File.Path))
                    evicted.Add(stale.File);
            }
        }

        return evicted;
    }

    private async Task MarkAppleMusicAsync(string? failureReason)
    {
        if (failureReason is not null) Volatile.Write(ref _appleMusicFailedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
        await _ui.TryRunAsync(() =>
        {
            if (failureReason is null) AppleMusicStatus.MarkAvailable();
            else AppleMusicStatus.MarkUnavailable(failureReason);
        }).ConfigureAwait(false);
    }

    private static string ReasonOf(Exception ex)
    {
        return ex switch
        {
            AppleMusicUnavailableException unavailable => unavailable.Reason,
            HttpRequestException or OperationCanceledException => "请求失败",
            _ => "其他原因"
        };
    }

    private static string? PreviewUrl(string? template)
    {
        return template?.Replace("{w}", "1080").Replace("{h}", "1080").Replace("{f}", "jpg");
    }

    private static async Task<StorageFolder> GetFolderAsync()
    {
        var root = await StorageMaintenance.GetCacheFolderAsync().ConfigureAwait(false);
        return await root.CreateFolderAsync(nameof(CacheType.DynamicCover), CreationCollisionOption.OpenIfExists);
    }

    private static async Task TryDeleteAsync(StorageFile file)
    {
        try
        {
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dynamic cover file delete failed: {ex.Message}");
        }
    }
}
