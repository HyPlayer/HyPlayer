using ALRC.Abstraction;
using HyPlayer.Classes.LyricParser.Abstraction;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace HyPlayer.Classes
{
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

    public class SongLyric
    {
        public static SongLyric PureSong = new()
        { LyricLine = new LrcLyricsLine("纯音乐 请欣赏", TimeSpan.Zero) };

        public static SongLyric NoLyric = new()
        { LyricLine = new LrcLyricsLine("无歌词 请欣赏", TimeSpan.Zero) };

        public static SongLyric LoadingLyric = new()
        { LyricLine = new LrcLyricsLine("加载歌词中...", TimeSpan.Zero) };

        public LyricLine LyricLine { get; set; }
        public string Translation { get; set; }
        public string Romaji { get; set; }

        public bool HaveTranslation => !string.IsNullOrEmpty(Translation);
        public bool HaveRomaji => !string.IsNullOrEmpty(Romaji);
    }

}
