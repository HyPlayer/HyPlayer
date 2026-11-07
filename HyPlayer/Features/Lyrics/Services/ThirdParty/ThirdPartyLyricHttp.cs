using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal static class ThirdPartyLyricHttp
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(8);
    private const int MaxBusyRetries = 3;

    private static readonly Lazy<string> LazyUserAgent = new(() =>
    {
        var version = Package.Current.Id.Version;
        return $"HyPlayer/{version.Major}.{version.Minor}.{version.Build}.{version.Revision} (https://github.com/HyPlayer/HyPlayer)";
    });

    public static string UserAgent => LazyUserAgent.Value;

    public static async Task<T?> GetJsonAsync<T>(
        HttpClient httpClient,
        string url,
        JsonTypeInfo<T> typeInfo,
        bool retryWhenBusy,
        CancellationToken ct) where T : class
    {
        for (var attempt = 0;; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (retryWhenBusy && attempt < MaxBusyRetries && RetryDelay(response, attempt) is { } delay)
                {
                    await Task.Delay(delay, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                return await JsonSerializer.DeserializeAsync(stream, typeInfo, timeout.Token);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Lyric request timed out: {url}", ex);
            }
        }
    }

    internal static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
            return null;
        var backoff = TimeSpan.FromSeconds(1 << attempt);
        var retryAfter = response.Headers.RetryAfter?.Delta
                         ?? (response.Headers.RetryAfter?.Date is { } date ? (TimeSpan?)(date - DateTimeOffset.UtcNow) : null);
        var delay = retryAfter > backoff ? retryAfter.Value : backoff;
        if (delay > MaxRetryDelay) return null;
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * delay.TotalMilliseconds * 0.3);
        return delay + jitter;
    }
}
