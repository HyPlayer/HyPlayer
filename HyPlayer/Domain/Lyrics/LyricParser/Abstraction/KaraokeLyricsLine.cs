using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

public sealed class KaraokeLyricsLine : LyricLine
{
    public KaraokeLyricsLine(IEnumerable<KaraokeWordInfo> wordInfos, string lyricWithoutPunc, TimeSpan startTime,
        TimeSpan duration)
    {
        WordInfos = wordInfos.ToList();
        StartTime = startTime;
        Duration = duration;
        LyricWithoutPunc = lyricWithoutPunc;
        CurrentLyric = string.Concat(WordInfos.Select(t => t.CurrentWords).ToArray());
    }

    [JsonConstructor]
    public KaraokeLyricsLine(
        List<KaraokeWordInfo> wordInfos,
        TimeSpan duration,
        string currentLyric,
        string lyricWithoutPunc,
        TimeSpan startTime,
        TimeSpan? possibleStartTime)
    {
        this.WordInfos = wordInfos;
        this.StartTime = startTime;
        this.PossibleStartTime = possibleStartTime;
        this.Duration = duration;
        this.LyricWithoutPunc = lyricWithoutPunc;
        this.CurrentLyric = currentLyric;
    }

    public List<KaraokeWordInfo> WordInfos { get; set; }
    public TimeSpan Duration { get; set; }
}