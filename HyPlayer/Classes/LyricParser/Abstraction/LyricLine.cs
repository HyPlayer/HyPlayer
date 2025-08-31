using System;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public class LyricLine
    {
        public string CurrentLyric { get; set; }
        public string LyricWithoutPunc { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan? PossibleStartTime { get; set; }
    }
}
