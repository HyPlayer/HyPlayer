using System.Collections.Generic;

namespace HyPlayer.Classes;

public class LyricInfo
{
    public List<SongLyric> Lyrics { get; set; } = [];
    public List<LyricInfoMetadata> LyricMetadata { get; set; } = [];
    public List<LyricInfoMetadata> SongMetadata { get; set; } = [];
    public PureLyricInfo PureLyricInfo { get; set; }
}

public class LyricInfoMetadata
{
    public string Key { get; set; }
    public string Value { get; set; }
    public string DisplayName { get; set; }
    public string ActionUri { get; set; }
}