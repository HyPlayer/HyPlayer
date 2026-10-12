using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Platform.Network;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class NeteaseSongUrlResolver
{
    private const string FetchFailedMessage = "下载链接获取失败";
    private static readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(20);

    private readonly ApiSettings _apiSettings;
    private readonly ConcurrentDictionary<string, CachedSongUrl> _cache = new();
    private readonly PlaybackSettings _playbackSettings;
    private readonly NeteaseProvider.NeteaseProvider _provider;

    public NeteaseSongUrlResolver(
        NeteaseProvider.NeteaseProvider provider,
        PlaybackSettings playbackSettings,
        ApiSettings apiSettings)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _playbackSettings = playbackSettings ?? throw new ArgumentNullException(nameof(playbackSettings));
        _apiSettings = apiSettings ?? throw new ArgumentNullException(nameof(apiSettings));
    }

    public Task<ResolvedSongUrl?> ResolveAsync(string songId, CancellationToken ct)
    {
        return ResolveAsync(songId, _playbackSettings.AudioRate, ct);
    }

    public async Task<ResolvedSongUrl?> ResolveAsync(string songId, string level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(songId))
            return null;

        var cacheKey = $"{songId}_{level}";
        if (!_cache.TryGetValue(cacheKey, out var cached) || DateTime.UtcNow - cached.FetchedAt >= _cacheLifetime)
        {
            var item = await RetryPolicies.UrlFetch
                .ExecuteAsync(() => RequestAsync(songId, level, ct))
                .ConfigureAwait(false);
            cached = new CachedSongUrl(item, DateTime.UtcNow);
            _cache[cacheKey] = cached;
            PruneExpired();
        }

        var songUrl = cached.Item;
        if (songUrl.FreeTrialInfo is not null && _apiSettings.JumpVipSongPlaying)
            return null;

        var url = songUrl.Url;
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (_apiSettings.UseHttpWhenGettingSongs && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url["https://".Length..];

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new ResolvedSongUrl(uri, songUrl)
            : null;
    }

    private async Task<SongUrlResponse.SongUrlItem> RequestAsync(string songId, string level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _provider
            .RequestAsync(NeteaseApis.SongUrlApi, new SongUrlRequest { Id = songId, Level = level }, ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (result.IsError)
            throw new InvalidOperationException(FetchFailedMessage, result.Error);

        if (result.Value?.SongUrls is not { Length: > 0 } songUrls || songUrls[0].Code != 200)
            throw new InvalidOperationException(FetchFailedMessage);

        return songUrls[0];
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _cache)
            if (now - pair.Value.FetchedAt >= _cacheLifetime)
                _cache.TryRemove(pair);
    }

    private readonly record struct CachedSongUrl(SongUrlResponse.SongUrlItem Item, DateTime FetchedAt);
}

public sealed record ResolvedSongUrl(Uri Uri, SongUrlResponse.SongUrlItem Item);
