using System.Collections.Generic;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

public interface ILyricCollection
{
    IList<LyricLine> Lines { get; }
}