using Windows.Storage;
using HyPlayer.Classes;

namespace HyPlayer.HyPlayControl
{
    public class HyPlayItem
    {
        public AudioInfo AudioInfo;
        public HyPlayItemType ItemType;
        public NCPlayItem NcPlayItem;

        public NCSong ToNCSong()
        {
            return NcPlayItem.ToNCSong();
        }
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
        Local,
        Netease,
        Pan,
        Radio
    }
}