using System;
using HyPlayer.Domain.Lyrics;

namespace HyPlayer.UI.Dialogs;

public class LyricShareItem
{
    public SongLyric OriginalLyric { get; set; }
    public string Text { get; set; }
    public TimeSpan Time { get; set; }
    public LyricShareItemType Type { get; set; }
}

public enum LyricShareItemType
{
    Original,
    Translation,
    Romaji
}
