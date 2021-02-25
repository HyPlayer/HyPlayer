using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;

namespace HyPlayer.HyPlayControl
{
    public class HyPlayItem
    {
        public string Name;
        public HyPlayItemType ItemType;
        public string Path;
        public bool isOnline;
        public MediaPlaybackItem MediaItem;
        public AudioInfo AudioInfo;
        public NCPlayItem NcPlayItem;
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
        public StorageFile LocalSongFile;
        public BitmapImage BitmapImage;
        public RandomAccessStreamReference Thumbnail;
    }

    public enum HyPlayItemType
    {
        Local, Netease, Pan, FM
    }
}
