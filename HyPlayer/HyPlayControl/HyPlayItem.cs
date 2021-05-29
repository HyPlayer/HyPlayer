using HyPlayer.Classes;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.HyPlayControl
{
    public class HyPlayItem
    {
        public string Name;
        public HyPlayItemType ItemType;
        public string Path;
        public bool isOnline;
        public AudioInfo AudioInfo;
        public NCPlayItem NcPlayItem;

        public NCSong ToNCSong() => NcPlayItem.ToNCSong();
    }

    public struct AudioInfo
    {
        public string SongName;
        public string Artist;
        public string[] ArtistArr;
        public string Album;
        public string Lyric;
        public string TrLyric;
        public double LengthInMilliseconds;
        public string Picture;
        public bool liked;
        public string tag;
        public StorageFile LocalSongFile;
    }

    public enum HyPlayItemType
    {
        Local, Netease, Pan, FM
    }
}
