using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.DynamicCover;

internal sealed class AppleMusicUnavailableException(string reason, Exception? inner = null)
    : Exception(reason, inner)
{
    public string Reason { get; } = reason;
}

internal sealed class AppleMusicCatalog(HttpClient http)
{
    private const string WebOrigin = "https://music.apple.com";
    private const string ApiOrigin = "https://amp-api.music.apple.com";
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private AppleMusicToken? _token;

    public async Task EnsureTokenAsync(CancellationToken ct)
    {
        await GetTokenAsync(false, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AppleMusicSongCandidate>> SearchSongsAsync(
        string storefront,
        string language,
        string term,
        CancellationToken ct)
    {
        var json = await GetAsync(
            $"/v1/catalog/{storefront}/search?term={Uri.EscapeDataString(term)}&types=songs&limit=10&l={language}",
            ct).ConfigureAwait(false);
        return AppleMusicMatcher.ParseSearchSongs(json);
    }

    public async Task<IReadOnlyList<AppleMusicAlbumCandidate>> GetAlbumsByIsrcAsync(
        string storefront,
        string language,
        string isrc,
        CancellationToken ct)
    {
        var json = await GetAsync(
            $"/v1/catalog/{storefront}/songs?filter[isrc]={Uri.EscapeDataString(isrc)}&include=albums&extend[albums]=editorialVideo&l={language}",
            ct).ConfigureAwait(false);
        return AppleMusicMatcher.ParseIsrcAlbums(json);
    }

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(attempt > 0, ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiOrigin + path);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.Value);
            request.Headers.TryAddWithoutValidation("Origin", WebOrigin);
            request.Headers.TryAddWithoutValidation("Referer", WebOrigin + "/");
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new AppleMusicUnavailableException("请求失败", ex);
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && attempt == 0)
                    continue;
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new AppleMusicUnavailableException("无法获取 Token");
                if (!response.IsSuccessStatusCode)
                    throw new AppleMusicUnavailableException("请求失败");
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<AppleMusicToken> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _token is { } cached && cached.ExpiresAt - TokenRefreshMargin > DateTimeOffset.UtcNow)
                return cached;
            _token = null;
            string html;
            string script;
            try
            {
                html = await http.GetStringAsync(WebOrigin + "/cn/new", ct).ConfigureAwait(false);
                var scriptPath = AppleMusicWebToken.FindScriptPath(html)
                                 ?? throw new AppleMusicUnavailableException("无法获取 Token");
                script = await http.GetStringAsync(new Uri(new Uri(WebOrigin), scriptPath), ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new AppleMusicUnavailableException("请求失败", ex);
            }

            var token = AppleMusicWebToken.FindToken(script, DateTimeOffset.UtcNow)
                        ?? throw new AppleMusicUnavailableException("无法获取 Token");
            _token = token;
            return token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }
}
