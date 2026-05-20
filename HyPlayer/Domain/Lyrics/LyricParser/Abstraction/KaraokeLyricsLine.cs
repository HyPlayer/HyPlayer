using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction
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

        [JsonConstructor]
        public KaraokeLyricsLine(
            List<KaraokeWordInfo> WordInfos,
            TimeSpan Duration,
            string CurrentLyric,
            string LyricWithoutPunc,
            TimeSpan StartTime,
            TimeSpan? PossibleStartTime)
        {
            this.WordInfos = WordInfos;
            this.StartTime = StartTime;
            this.PossibleStartTime = PossibleStartTime;
            this.Duration = Duration;
            this.LyricWithoutPunc = LyricWithoutPunc;
            this.CurrentLyric = CurrentLyric;
        }
    }
}
