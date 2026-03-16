using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyPlayer.Classes
{
    public partial class PlayItem : IDisposable
    {
        private bool disposedValue;
        public AudioGraphPlaybackSource AudioGraphPlaybackSource { get; set; }
        public InMemoryRandomAccessStream NcmPlayableStream { get; set; }
        public string NcmPlayableStreamMIMEType { get; set; } = string.Empty;
        
        public PlayItem()
        {

        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    AudioGraphPlaybackSource?.Dispose();
                }
                NcmPlayableStream?.Dispose();
                NcmPlayableStreamMIMEType = null;
                AudioGraphPlaybackSource = null;
                NcmPlayableStream = null;
                disposedValue = true;
            }
        }

        ~PlayItem()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    public class HyPlayItem : IEquatable<HyPlayItem>
    {
        public PlayItem PlayItem { get; set; }
        public HyPlayItemType ItemType { get; set; }
        public NCAlbum Album { get; set; }
        public List<NCArtist> Artist { get; set; }
        public int Bitrate { get; set; }
        public string CDName { get; set; }
        public string Translation { get; set; }
        public StorageFile LocalStorageFile { get; set; } //如非特殊原因请不要设置这个东西!
        public Tag LocalFileTag { get; set; }
        public string Id { get; set; }
        public bool IsLocalFile { get; set; }
        public double LengthInMilliseconds { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public string SubExt { get; set; }
        public string QualityTag { get; set; }
        public string InfoTag { get; set; }
        public int TrackId { get; set; }
        public string Url { get; set; }
        public double? Volume { get; set; }

        public string ArtistString
        {
            get { return string.Join("; ", Artist.Select(t => t.Name)); }
        }
        public string AlbumString => Album.Name ?? "未知专辑";

        public bool Equals(HyPlayItem other)
        {
            return other.Id == Id;
        }

        public NCSong ToNCSong()
        {
            return new NCSong
            {
                Type = ItemType,
                Album = Album,
                Artist = Artist,
                LengthInMilliseconds = LengthInMilliseconds,
                SongId = Id,
                SongName = Name,
                TrackId = TrackId
            };
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HyPlayItem);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
    public enum HyPlayItemType
    {
        Local,
        LocalProgressive,
        Netease,
        Radio
    }
}
