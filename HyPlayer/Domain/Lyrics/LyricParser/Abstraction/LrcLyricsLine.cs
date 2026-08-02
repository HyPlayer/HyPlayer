using System;
using System.Text;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

public sealed class LrcLyricsLine : LyricLine
{
    public LrcLyricsLine(string currentLyric, TimeSpan startTime)
    {
        CurrentLyric = currentLyric;
        StartTime = startTime;
        var builder = new StringBuilder();
        foreach (var curChar in CurrentLyric)
            if (!char.IsPunctuation(curChar) && !char.IsWhiteSpace(curChar))
                builder.Append(curChar);

        LyricWithoutPunc = builder.ToString();
    }
}