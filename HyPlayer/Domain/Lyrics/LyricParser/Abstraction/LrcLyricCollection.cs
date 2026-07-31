using System;
using System.Collections.Generic;

namespace HyPlayer.Domain.Lyrics.LyricParser.Abstraction;

public sealed class LrcLyricCollection : ILyricCollection
{
    private bool _disposedValue;

    public LrcLyricCollection(IList<LyricLine> lines, IList<KeyValuePair<string, string>> attributes)
    {
        Lines = lines;
        Attributes = attributes;
    }

    public IList<KeyValuePair<string, string>> Attributes { get; }
    public IList<LyricLine> Lines { get; }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Lines.Clear();
                Attributes.Clear();
            }

            _disposedValue = true;
        }
    }

    ~LrcLyricCollection()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}