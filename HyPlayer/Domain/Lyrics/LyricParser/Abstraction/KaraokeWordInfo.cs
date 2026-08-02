#nullable enable
using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

[DebuggerDisplay("Word = {CurrentWords}, Transliteration = {Transliteration}")]
public sealed class KaraokeWordInfo
{
    public KaraokeWordInfo(string currentWords, TimeSpan startTime, TimeSpan duration)
    {
        CurrentWords = currentWords;
        StartTime = startTime;
        Duration = duration;
    }

    [JsonConstructor]
    public KaraokeWordInfo(string currentWords, TimeSpan startTime, TimeSpan duration, string? transliteration)
    {
        this.CurrentWords = currentWords;
        this.StartTime = startTime;
        this.Duration = duration;
        this.Transliteration = transliteration;
    }

    public string CurrentWords { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Transliteration { get; set; }
}