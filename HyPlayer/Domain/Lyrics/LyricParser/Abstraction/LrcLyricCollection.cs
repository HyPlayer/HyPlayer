using System;
using System.Collections.Generic;

namespace HyPlayer.Classes.LyricParser.Abstraction
{
    public sealed class LrcLyricCollection : ILyricCollection
    {
        private bool disposedValue;
        public IList<LyricLine> Lines { get; }
        public IList<KeyValuePair<string, string>> Attributes { get; }
        public LrcLyricCollection(IList<LyricLine> lines, IList<KeyValuePair<string, string>> attributes)
        {
            Lines = lines;
            Attributes = attributes;
        }
        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Lines.Clear();
                    Attributes.Clear();
                }
                disposedValue = true;
            }
        }

        ~LrcLyricCollection()
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
