using System;
using System.Collections.Generic;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public sealed class MigrateCollection : ILyricCollection
    {
        private bool disposedValue;
        public IList<LyricLine> Lines { get; }
        public MigrateCollection(IList<LyricLine> lines)
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

        ~MigrateCollection()
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
