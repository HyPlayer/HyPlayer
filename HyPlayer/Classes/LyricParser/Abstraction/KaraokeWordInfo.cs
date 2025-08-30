using System;
using System.Diagnostics;
using Newtonsoft.Json;
#nullable enable
namespace HyPlayer.Classes.LyricParser.Abstraction
{
    [DebuggerDisplay("Word = {CurrentWords}, Transliteration = {Transliteration}")]
    public sealed class KaraokeWordInfo
    {
        public string CurrentWords { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string? Transliteration { get; set; }

        public KaraokeWordInfo(string currentWords, TimeSpan startTime, TimeSpan duration)
        {
            CurrentWords = currentWords;
            StartTime = startTime;
            Duration = duration;
        }

        [JsonConstructor]
        public KaraokeWordInfo(string CurrentWords, TimeSpan StartTime, TimeSpan Duration, string? Transliteration)
        {
            this.CurrentWords = CurrentWords;
            this.StartTime = StartTime;
            this.Duration = Duration;
            this.Transliteration = Transliteration;
        }
    }
}
