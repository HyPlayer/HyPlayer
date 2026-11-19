using System.Collections.Generic;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

public sealed class AmllSearchResponse
{
    public int Status { get; set; }
    public AmllSearchData? Data { get; set; }
}

public sealed class AmllSearchData
{
    public List<AmllSongItem>? Items { get; set; }
}

public sealed class AmllGetResponse
{
    public int Status { get; set; }
    public AmllSongItem? Data { get; set; }
}

public sealed class AmllSongItem
{
    public long Id { get; set; }
    public string? Filename { get; set; }
    public List<string>? MusicNames { get; set; }
    public List<string>? ArtistNames { get; set; }
    public string? Lyrics { get; set; }
}

public sealed class LrclibRecord
{
    public long Id { get; set; }
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public double Duration { get; set; }
    public bool Instrumental { get; set; }
    public string? SyncedLyrics { get; set; }
}

public sealed class KugouSearchResponse
{
    public int Status { get; set; }
    public List<KugouCandidate>? Candidates { get; set; }
}

public sealed class KugouCandidate
{
    public string? Id { get; set; }
    public string? Accesskey { get; set; }
    public string? Song { get; set; }
    public string? Singer { get; set; }
    public long Duration { get; set; }
}

public sealed class KugouDownloadResponse
{
    public int Status { get; set; }
    public int Contenttype { get; set; }
    public string? Content { get; set; }
}

internal sealed record ThirdPartyLyricText(string Text, string? SourceUri);

internal readonly record struct ThirdPartyLookup<T>(T? Lyric, bool Failed) where T : class;
