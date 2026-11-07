using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

public sealed class ThirdPartyLyricSources
{
    private readonly LyricSettings _setting;
    private readonly AmllLyricClient _amll;
    private readonly KugouLyricClient _kugou;
    private readonly LrclibLyricClient _lrclib;
    private readonly HashSet<string> _missingSongs = [];

    public ThirdPartyLyricSources(HttpClient httpClient, LyricSettings setting)
    {
        _setting = setting;
        _amll = new AmllLyricClient(httpClient, setting);
        _kugou = new KugouLyricClient(httpClient);
        _lrclib = new LrclibLyricClient(httpClient, setting);
    }

    public bool AllowsSong(SingleSongBase item)
    {
        return item switch
        {
            NeteaseRadioProgram => _setting.ThirdPartyLyricsForPrograms,
            NeteaseCloudLibraryItem or NeteaseSong { IsCloudSong: true } => _setting.ThirdPartyLyricsForCloud,
            LocalSong => _setting.ThirdPartyLyricsForLocal,
            NeteaseSong => _setting.ThirdPartyLyricsForSongs,
            _ => false
        };
    }

    public static bool SearchesWithOwnLyrics(SingleSongBase item)
    {
        return item is NeteaseRadioProgram or NeteaseCloudLibraryItem or LocalSong or NeteaseSong { IsCloudSong: true };
    }

    public bool CanFetchAmllById(SingleSongBase item, string? neteaseId, bool manual = false)
    {
        return _setting.EnableAmllTtmlDb &&
               !string.IsNullOrWhiteSpace(neteaseId) &&
               (manual || AllowsSong(item)) &&
               !_amll.IsKnownMissing(neteaseId);
    }

    public bool CanSearch(SingleSongBase item)
    {
        return (_setting.EnableAmllTtmlDb || _setting.EnableKugou || _setting.EnableLrclib) && AllowsSong(item) &&
               !IsKnownMissing(item);
    }

    public bool AmllEnabled => _setting.EnableAmllTtmlDb;

    public bool KugouEnabled => _setting.EnableKugou;

    public bool LrclibEnabled => _setting.EnableLrclib;

    public Task<string?> FetchAmllByIdAsync(string id, CancellationToken ct)
    {
        return _amll.FetchByNcmIdAsync(id, ct);
    }

    internal Task<ThirdPartyLookup<ThirdPartyLyricText>> SearchAmllAsync(LyricSearchQuery query, CancellationToken ct)
    {
        return LookupAsync(() => _amll.SearchAsync(query, ct), "AMLL", ct);
    }

    internal Task<ThirdPartyLookup<PureLyricInfo>> SearchKugouAsync(LyricSearchQuery query, CancellationToken ct)
    {
        return LookupAsync(() => _kugou.SearchAsync(query, ct), "Kugou", ct);
    }

    internal Task<ThirdPartyLookup<ThirdPartyLyricText>> GetLrclibAsync(LyricSearchQuery query, CancellationToken ct)
    {
        return LookupAsync(() => _lrclib.GetAsync(query, ct), "LRCLIB", ct);
    }

    public void MarkMissing(SingleSongBase item)
    {
        lock (_missingSongs)
        {
            _missingSongs.Add(SongKey(item));
        }
    }

    private bool IsKnownMissing(SingleSongBase item)
    {
        lock (_missingSongs)
        {
            return _missingSongs.Contains(SongKey(item));
        }
    }

    private static string SongKey(SingleSongBase item)
    {
        return $"{item.ProviderId}:{item.TypeId}:{item.ActualId}";
    }

    private static async Task<ThirdPartyLookup<T>> LookupAsync<T>(
        Func<Task<T?>> lookup,
        string sourceName,
        CancellationToken ct) where T : class
    {
        try
        {
            return new ThirdPartyLookup<T>(await lookup(), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException or IOException
                                       or InvalidDataException or FormatException)
        {
            Debug.WriteLine($"{sourceName} lyric lookup failed: {ex.Message}");
            return new ThirdPartyLookup<T>(null, true);
        }
    }
}
