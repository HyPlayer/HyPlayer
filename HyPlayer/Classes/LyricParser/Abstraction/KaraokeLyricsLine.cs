using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public sealed class KaraokeLyricsLine : LyricLine
    {
        public List<KaraokeWordInfo> WordInfos { get; set; }
        public TimeSpan Duration { get; set; }

        public KaraokeLyricsLine(IEnumerable<KaraokeWordInfo> wordInfos, string lyricWithoutPunc, TimeSpan startTime, TimeSpan duration)
        {
            WordInfos = wordInfos.ToList();
            StartTime = startTime;
            Duration = duration;
            LyricWithoutPunc = lyricWithoutPunc;
            CurrentLyric = string.Concat(WordInfos.Select(t => t.CurrentWords).ToArray());
        }
    }
}
