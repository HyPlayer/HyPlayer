using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal sealed class AmllLyricClient(HttpClient httpClient, LyricSettings setting)
{
    private const string ApiBase = "https://api.amll.dev";
    private const string RepositoryUrl = "https://github.com/amll-dev/amll-ttml-db";
    private static readonly TimeSpan MirrorTimeout = TimeSpan.FromSeconds(8);

    private static readonly string[] FallbackMirrors =
    [
        LyricSettings.DefaultAmllTtmlMirrorUrl,
        "https://gcore.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/ncm-lyrics/[NCM_ID].ttml",
        "https://amll-ttml-db.stevexmh.net/ncm/[NCM_ID]"
    ];

    private readonly HashSet<string> _missingIds = [];

    public static string NcmLyricUri(string id) => $"{RepositoryUrl}/blob/main/ncm-lyrics/{id}.ttml";

    public bool IsKnownMissing(string id)
    {
        lock (_missingIds)
        {
            return _missingIds.Contains(id);
        }
    }

    public async Task<string?> FetchByNcmIdAsync(string id, CancellationToken ct)
    {
        var mirrors = new List<string> { setting.AmllTtmlMirrorUrl };
        foreach (var mirror in FallbackMirrors)
            if (!mirrors.Contains(mirror))
                mirrors.Add(mirror);

        var allMissing = true;
        foreach (var mirror in mirrors)
        {
            if (string.IsNullOrWhiteSpace(mirror) || !mirror.Contains("[NCM_ID]")) continue;
            var url = mirror.Replace("[NCM_ID]", id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(MirrorTimeout);
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                message.Headers.TryAddWithoutValidation("User-Agent", ThirdPartyLyricHttp.UserAgent);
                using var response = await httpClient.SendAsync(message, timeout.Token);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) continue;
                allMissing = false;
                if (!response.IsSuccessStatusCode) continue;
                var content = await response.Content.ReadAsStringAsync(timeout.Token);
                if (content.AsSpan().TrimStart().StartsWith("<tt", StringComparison.Ordinal)) return content;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                allMissing = false;
                Debug.WriteLine($"AMLL mirror request failed: {url} {ex.Message}");
            }
        }

        if (allMissing)
            lock (_missingIds)
            {
                _missingIds.Add(id);
            }

        return null;
    }

    public async Task<ThirdPartyLyricText?> SearchAsync(LyricSearchQuery query, CancellationToken ct)
    {
        var titleUrl = $"{ApiBase}/v1/lyrics/search?musicName={Uri.EscapeDataString(query.Title)}&pageSize=10";
        var item = await PickAsync(query, $"{titleUrl}&artistName={Uri.EscapeDataString(query.PrimaryArtist)}", ct)
                   ?? await PickAsync(query, titleUrl, ct);
        if (item is null) return null;

        var detail = await ThirdPartyLyricHttp.GetJsonAsync(httpClient, $"{ApiBase}/v1/lyrics/get?id={item.Id}",
            JsonDefaultContext.Default.AmllGetResponse, false, ct);
        var ttml = detail?.Data?.Lyrics;
        if (string.IsNullOrWhiteSpace(ttml) || !ttml.AsSpan().TrimStart().StartsWith("<tt", StringComparison.Ordinal))
            return null;
        var fileName = detail?.Data?.Filename ?? item.Filename;
        return new ThirdPartyLyricText(ttml,
            string.IsNullOrEmpty(fileName) ? RepositoryUrl : $"{RepositoryUrl}/blob/main/raw-lyrics/{fileName}");
    }

    private async Task<AmllSongItem?> PickAsync(LyricSearchQuery query, string url, CancellationToken ct)
    {
        var search = await ThirdPartyLyricHttp.GetJsonAsync(httpClient, url,
            JsonDefaultContext.Default.AmllSearchResponse, false, ct);
        return search?.Data?.Items is { Count: > 0 } items ? ThirdPartyLyricMatcher.PickAmll(query, items) : null;
    }
}
