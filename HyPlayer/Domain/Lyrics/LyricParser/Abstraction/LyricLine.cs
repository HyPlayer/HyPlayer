using System;
using System.Text.Json.Serialization;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

[JsonDerivedType(typeof(KaraokeLyricsLine), "karaoke")]
[JsonDerivedType(typeof(LrcLyricsLine), "lrc")]
public class LyricLine
{
    public string CurrentLyric { get; set; }
    public string LyricWithoutPunc { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan? PossibleStartTime { get; set; }
}