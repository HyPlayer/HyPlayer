using System;
using System.Collections.Generic;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

public sealed class KaraokeLyricCollection : ILyricCollection
{
    private bool disposedValue;

    public KaraokeLyricCollection(IList<LyricLine> lines)
    {
        Lines = lines;
    }

    public IList<LyricLine> Lines { get; }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing) Lines.Clear();
            disposedValue = true;
        }
    }

    ~KaraokeLyricCollection()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}