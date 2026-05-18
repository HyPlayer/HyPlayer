using System;
using System.Collections.Generic;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public sealed class KaraokeLyricCollection : ILyricCollection
    {
        private bool disposedValue;

        public IList<LyricLine> Lines { get; }
        public KaraokeLyricCollection(IList<LyricLine> lines)
        {
            Lines = lines;
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Lines.Clear();
                }
                disposedValue = true;
            }
        }

        ~KaraokeLyricCollection()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
