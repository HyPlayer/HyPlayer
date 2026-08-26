﻿using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ALRC.Abstraction;
namespace HyPlayer.Domain.Lyrics;

public class KaraokLyricInfo : PureLyricInfo
{
    public string KaraokLyric { get; set; }
    public string YrTrLyrics { get; set; }
    public string YrNeteaseRomaji { get; set; }
}

public class HyALRCLyricInfo : PureLyricInfo
{
    public ALRCFile ALRC { get; set; }
}

[JsonDerivedType(typeof(HyALRCLyricInfo), "ALRC")]
[JsonDerivedType(typeof(KaraokLyricInfo), "Karaok")]
public class PureLyricInfo
{
    public string PureLyrics { get; set; }
    public string TrLyrics { get; set; }
    public string NeteaseRomaji { get; set; }
    public List<LyricInfoMetadata> SongMetadata { get; set; } = [];
    public List<LyricInfoMetadata> LyricMetadata { get; set; } = [];
}

public sealed class SongLyric
{
    public static SongLyric PureSong { get; } = new() { Text = "纯音乐 请欣赏" };

    public static SongLyric NoLyric { get; } = new() { Text = "无歌词 请欣赏" };

    public static SongLyric LoadingLyric { get; } = new() { Text = "加载歌词中..." };

    public string Text { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan? MatchedStartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<LyricSyllable>? Syllables { get; set; }
    public string? Translation { get; set; }
    public string? Romaji { get; set; }

    public bool IsSyllableSynced => Syllables is { Count: > 0 };
    public bool HaveTranslation => !string.IsNullOrEmpty(Translation);
    public bool HaveRomaji => !string.IsNullOrEmpty(Romaji);
}

public sealed class LyricSyllable
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Transliteration { get; set; }
}
