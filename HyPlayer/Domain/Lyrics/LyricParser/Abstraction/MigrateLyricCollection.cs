using System;
using System.Collections.Generic;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

public sealed class MigrateCollection : ILyricCollection
{
    private bool _disposedValue;

    public MigrateCollection(IList<LyricLine> lines)
    {
        Lines = lines;
    }

    public IList<LyricLine> Lines { get; }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing) Lines.Clear();
            _disposedValue = true;
        }
    }

    ~MigrateCollection()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}