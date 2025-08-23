using System;
using System.Diagnostics;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    [DebuggerDisplay("Word = {CurrentWords}, Transliteration = {Transliteration}")]
    public sealed class KaraokeWordInfo
    {
        public string CurrentWords { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string? Transliteration;
        public KaraokeWordInfo(string currentWords, TimeSpan startTime, TimeSpan duration)
        {
            CurrentWords = currentWords;
            StartTime = startTime;
            Duration = duration;
        }


    }
}
