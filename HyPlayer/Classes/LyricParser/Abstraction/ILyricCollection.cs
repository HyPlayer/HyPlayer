using System;
using System.Collections.Generic;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public interface ILyricCollection : IDisposable
    {
        IList<LyricLine> Lines { get; }
    }
}
