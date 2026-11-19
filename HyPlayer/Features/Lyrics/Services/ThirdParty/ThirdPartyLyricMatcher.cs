using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.Domain.Lyrics;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal static class ThirdPartyLyricMatcher
{
    private const long LastLineToleranceMs = 5_000;

    public static AmllSongItem? PickAmll(LyricSearchQuery query, IEnumerable<AmllSongItem> items)
    {
        return items
            .Select((item, index) => (item, index,
                level: TrackMatchScorer.Score(query, item.MusicNames, item.ArtistNames, null)))
            .Where(entry => TrackMatchScorer.Accepts(entry.level))
            .OrderByDescending(entry => entry.level)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .FirstOrDefault();
    }

    public static bool AcceptLrclib(LyricSearchQuery query, LrclibRecord record)
    {
        if (record.Instrumental || string.IsNullOrWhiteSpace(record.SyncedLyrics)) return false;
        return TrackMatchScorer.Accepts(TrackMatchScorer.Score(query, [record.TrackName], [record.ArtistName],
            (long)Math.Round(record.Duration * 1000)));
    }

    public static bool FitsDuration(LyricSearchQuery query, HyLyricInfo lyricInfo)
    {
        if (query.DurationMs <= 0) return true;
        var lastLine = lyricInfo.Lyrics
            .Where(line => !string.IsNullOrWhiteSpace(line.LyricLine.CurrentLyric))
            .Select(line => line.LyricLine.StartTime)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        return lastLine.TotalMilliseconds <= query.DurationMs + LastLineToleranceMs;
    }
}
