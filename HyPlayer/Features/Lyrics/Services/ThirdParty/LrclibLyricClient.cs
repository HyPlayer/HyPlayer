using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal sealed class LrclibLyricClient(HttpClient httpClient, LyricSettings setting)
{
    public async Task<ThirdPartyLyricText?> GetAsync(LyricSearchQuery query, CancellationToken ct)
    {
        if (query.DurationMs <= 0) return null;
        var baseUrl = BaseUrl();
        var seconds = Math.Round(query.DurationMs / 1000.0).ToString(CultureInfo.InvariantCulture);
        var url = $"{baseUrl}/api/get?track_name={Uri.EscapeDataString(query.Title)}" +
                  $"&artist_name={Uri.EscapeDataString(query.PrimaryArtist)}&duration={seconds}";
        var record = await ThirdPartyLyricHttp.GetJsonAsync(httpClient, url,
            JsonDefaultContext.Default.LrclibRecord, true, ct);
        if (record is null || !ThirdPartyLyricMatcher.AcceptLrclib(query, record)) return null;
        return new ThirdPartyLyricText(record.SyncedLyrics!, baseUrl);
    }

    private string BaseUrl()
    {
        var url = setting.LrclibUrl?.Trim().TrimEnd('/');
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? url!
            : LyricSettings.DefaultLrclibUrl;
    }
}
