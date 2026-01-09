using System.Collections.Generic;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public interface ILyricCollection
    {
        IList<LyricLine> Lines { get; }
    }
}
