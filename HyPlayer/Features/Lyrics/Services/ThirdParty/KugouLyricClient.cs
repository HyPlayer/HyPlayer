using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Domain.Lyrics;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal sealed class KugouLyricClient(HttpClient httpClient)
{
    public const string SourceName = "kugou";
    public const string SourceUri = "https://www.kugou.com/";
    private const string ApiBase = "https://lyrics.kugou.com";
    private const int MaxDownloads = 2;

    public async Task<PureLyricInfo?> SearchAsync(LyricSearchQuery query, CancellationToken ct)
    {
        var candidates = await FindCandidatesAsync(query, $"{query.PrimaryArtist} - {query.Title}", ct);
        if (candidates.Count == 0)
            candidates = await FindCandidatesAsync(query, query.Title, ct);

        foreach (var candidate in candidates.Take(MaxDownloads))
        {
            if (await DownloadAsync(candidate, ct) is not { } krc) continue;
            var document = KrcLyricParser.Parse(krc);
            if (document.Lines.Any(line => !string.IsNullOrWhiteSpace(line.Text)))
                return KrcLyricParser.ToLyricInfo(document, query, SourceUri);
        }

        return null;
    }

    internal static List<KugouCandidate> RankCandidates(LyricSearchQuery query, IEnumerable<KugouCandidate> items)
    {
        return items
            .Where(item => !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Accesskey))
            .Select((item, index) => (item, index,
                level: TrackMatchScorer.Score(query, [item.Song], [item.Singer], item.Duration)))
            .Where(entry => TrackMatchScorer.Accepts(entry.level))
            .OrderByDescending(entry => entry.level)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .ToList();
    }

    private async Task<List<KugouCandidate>> FindCandidatesAsync(LyricSearchQuery query, string keyword,
        CancellationToken ct)
    {
        var url = $"{ApiBase}/search?ver=1&man=yes&client=pc&keyword={Uri.EscapeDataString(keyword)}&hash=" +
                  (query.DurationMs > 0 ? $"&duration={query.DurationMs}" : string.Empty);
        var response = await ThirdPartyLyricHttp.GetJsonAsync(httpClient, url,
            JsonDefaultContext.Default.KugouSearchResponse, true, ct);
        return response?.Candidates is { Count: > 0 } items ? RankCandidates(query, items) : [];
    }

    private async Task<string?> DownloadAsync(KugouCandidate candidate, CancellationToken ct)
    {
        var url = $"{ApiBase}/download?ver=1&client=pc&fmt=krc&charset=utf8" +
                  $"&id={Uri.EscapeDataString(candidate.Id!)}&accesskey={Uri.EscapeDataString(candidate.Accesskey!)}";
        var response = await ThirdPartyLyricHttp.GetJsonAsync(httpClient, url,
            JsonDefaultContext.Default.KugouDownloadResponse, true, ct);
        if (response is not { Status: 200, Contenttype: 0 } || string.IsNullOrEmpty(response.Content)) return null;
        return KrcLyricParser.Decrypt(Convert.FromBase64String(response.Content));
    }
}
