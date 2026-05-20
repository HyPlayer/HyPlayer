using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using System;
using System.Collections.Generic;
using System.Text;

namespace HyPlayer.Domain.Lyrics.LyricParser.Implementation
{
    public static class KaraokeParser
    {
        public static ILyricCollection ParseKaraoke(ReadOnlySpan<char> input)
        {
            List<LyricLine> lines = new List<LyricLine>();
            List<KaraokeWordInfo> karaokeWordInfos = new List<KaraokeWordInfo>();
            var timeSpanBuilder = 0;
            var lyricStringBuilder = new StringBuilder();
            var lyricWithoutPuncBuilder = new StringBuilder();
            var lyricTimespan = 0;
            var lyricDuration = 0;
            var wordTimespan = 0;
            var wordDuration = 0;
            var state = CurrentState.None;
            var reachesEnd = false;
            for (var i = 0; i < input.Length; i++)
            {
                if (i != input.Length)
                {
                    ref readonly var curChar = ref input[i];

                    if (curChar == '\n' || curChar == '\r' || i + 1 == input.Length)
                    {
                        if (i + 1 < input.Length)
                        {
                            if (input[i + 1] == '\n' || input[i + 1] == '\r') i++;
                            karaokeWordInfos.Add(new KaraokeWordInfo(lyricStringBuilder.ToString(), TimeSpan.FromMilliseconds(wordTimespan), TimeSpan.FromMilliseconds(wordDuration)));
                            lines.Add(new KaraokeLyricsLine(karaokeWordInfos, lyricWithoutPuncBuilder.ToString(), TimeSpan.FromMilliseconds(lyricTimespan), TimeSpan.FromMilliseconds(lyricDuration)));
                            karaokeWordInfos.Clear();
                            lyricWithoutPuncBuilder.Clear();
                            lyricStringBuilder.Clear();
                            state = CurrentState.None;
                            continue;
                        }
                        if (i + 1 == input.Length)
                        {
                            reachesEnd = true;
                        }
                    }
                    switch (curChar)
                    {
                        case '[':
                            if (state == CurrentState.Lyric)
                            {
                                if (i + 1 < input.Length)
                                {
                                    if (!char.IsNumber(input[i + 1])) break;
                                }
                            }
                            state = CurrentState.PossiblyLyricTimestamp;
                            continue;
                        case ',':
                            if (state == CurrentState.Lyric)
                            {
                                if (i + 1 < input.Length)
                                {
                                    if (!char.IsNumber(input[i + 1])) break;
                                }
                            }
                            if (state == CurrentState.LyricTimestamp)
                            {
                                state = CurrentState.PossiblyLyricDuration;
                                lyricTimespan = timeSpanBuilder;
                                timeSpanBuilder = 0;
                            }
                            else if (state == CurrentState.WordTimestamp)
                            {
                                state = CurrentState.PossiblyWordDuration;
                                wordTimespan = timeSpanBuilder;
                                timeSpanBuilder = 0;
                            }
                            else
                            {
                                state = CurrentState.WordUnknownItem;
                                wordDuration = timeSpanBuilder;
                                timeSpanBuilder = 0;
                            }
                            continue;
                        case ']':
                            if (state == CurrentState.Lyric)
                            {
                                if (i + 1 < input.Length)
                                {
                                    if (!char.IsNumber(input[i + 1])) break;
                                }
                            }
                            state = CurrentState.None;
                            lyricDuration = timeSpanBuilder;
                            timeSpanBuilder = 0;
                            continue;
                        case '(':
                            if (state == CurrentState.Lyric)
                            {
                                if (i + 1 < input.Length)
                                {
                                    if (!char.IsNumber(input[i + 1])) break;
                                }
                                karaokeWordInfos.Add(new KaraokeWordInfo(lyricStringBuilder.ToString(), TimeSpan.FromMilliseconds(wordTimespan), TimeSpan.FromMilliseconds(wordDuration)));
                                lyricStringBuilder.Clear();
                            }
                            state = CurrentState.PossiblyWordTimestamp;
                            continue;
                        case ')':
                            if (state == CurrentState.Lyric)
                            {
                                if (i + 1 < input.Length)
                                {
                                    if (!char.IsNumber(input[i + 1])) break;
                                }
                            }
                            state = CurrentState.Lyric;
                            continue;
                    }
                    switch (state)
                    {
                        case CurrentState.PossiblyLyricTimestamp:
                            if (char.IsNumber(curChar)) state = CurrentState.LyricTimestamp;
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.LyricTimestamp:
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.PossiblyWordTimestamp:
                            if (char.IsNumber(curChar)) state = CurrentState.WordTimestamp;
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.WordTimestamp:
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.PossiblyLyricDuration:
                            if (char.IsNumber(curChar)) state = CurrentState.LyricDuration;
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.LyricDuration:
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.PossiblyWordDuration:
                            if (char.IsNumber(curChar)) state = CurrentState.WordDuration;
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.WordDuration:
                            timeSpanBuilder *= 10;
                            timeSpanBuilder += curChar - '0';
                            break;
                        case CurrentState.Lyric:
                            if (reachesEnd && (input[i] == '\n' || input[i] == '\r')) break;
                            lyricStringBuilder.Append(curChar);
                            if (!char.IsPunctuation(curChar) && !char.IsWhiteSpace(curChar)) lyricWithoutPuncBuilder.Append(curChar);
                            break;
                    }
                    if (reachesEnd)
                    {
                        karaokeWordInfos.Add(new KaraokeWordInfo(lyricStringBuilder.ToString(), TimeSpan.FromMilliseconds(wordTimespan), TimeSpan.FromMilliseconds(wordDuration)));
                        lines.Add(new KaraokeLyricsLine(karaokeWordInfos, lyricWithoutPuncBuilder.ToString(), TimeSpan.FromMilliseconds(lyricTimespan), TimeSpan.FromMilliseconds(lyricDuration)));
                        lyricWithoutPuncBuilder.Clear();
                        karaokeWordInfos.Clear();
                        lyricStringBuilder.Clear();
                    }
                }
            }
            return new KaraokeLyricCollection(lines);
        }

        private enum CurrentState
        {
            None,
            LyricTimestamp,
            WordTimestamp,
            LyricDuration,
            WordDuration,
            WordUnknownItem,
            PossiblyLyricDuration,
            PossiblyWordDuration,
            PossiblyLyricTimestamp,
            PossiblyWordTimestamp,
            Lyric
        }
    }
}
