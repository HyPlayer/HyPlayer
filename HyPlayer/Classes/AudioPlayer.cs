using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using TagLib;
using File = System.IO.File;

namespace HyPlayer.Classes
{
    public class UwpStorageFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly StorageFile file;

        public string Name => file.Name;

        public Stream ReadStream
        {
            get
            {
                return file.OpenStreamForReadAsync().GetAwaiter().GetResult();
            }
        }

        public Stream WriteStream
        {
            get
            {
                return file.OpenStreamForWriteAsync().GetAwaiter().GetResult();
            }
        }


        public UwpStorageFileAbstraction(StorageFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            this.file = file;
        }


        public void CloseStream(Stream stream)
        {
            stream?.Dispose();
        }
    }

    class AudioPlayer
    {
        public static MediaPlayer AudioMediaPlayer;
        public static MediaPlaybackList AudioMediaPlaybackList;
        public static Timer AudioPlayerTimer;
        public static List<SongLyric> Lyrics = new List<SongLyric>();
        public static Dictionary<MediaPlaybackItem, AudioInfo> AudioInfos = new Dictionary<MediaPlaybackItem, AudioInfo>();
        public static Random AudioRandom = new Random();

        public static void LoadPureLyric(string LyricAllText)
        {
            Lyrics = new List<SongLyric>();
            if (string.IsNullOrEmpty(LyricAllText)) return;
            string[] LyricsArr = LyricAllText.Replace("\r\n", "\n").Replace("\r", "\n").Split("\n");
            TimeSpan offset = TimeSpan.Zero;
            foreach (string sL in LyricsArr)
            {
                string LyricTextLine = sL.Trim();
                if (LyricTextLine.IndexOf('[') == -1 || LyricTextLine.IndexOf(']') == -1)
                    continue; //此行不为Lrc
                string prefix = LyricTextLine.Substring(1, LyricTextLine.IndexOf(']') - 1);
                if (prefix.StartsWith("al") || prefix.StartsWith("ar") || prefix.StartsWith("au") ||
                    prefix.StartsWith("by") || prefix.StartsWith("re") || prefix.StartsWith("ti") ||
                    prefix.StartsWith("ve"))
                {//这种废标签不想解析
                    continue;
                }

                if (prefix.StartsWith("offset"))
                {
                    if (!int.TryParse(prefix.Substring(6), out int offsetint))
                        continue;
                    offset = new TimeSpan(0, 0, 0, 0, offsetint);
                }

                if (!TimeSpan.TryParse("00:" + prefix, out TimeSpan time))
                    continue;
                string lrctxt = LyricTextLine.Substring(LyricTextLine.IndexOf(']') + 1);
                //NLyric 的双语歌词 - 夹带私货
                string translation = null;
                if (LyricTextLine.IndexOf('「') != -1 && LyricTextLine.IndexOf('」') != -1)
                {
                    translation = LyricTextLine.Substring(LyricTextLine.IndexOf('「') + 1, LyricTextLine.IndexOf('」') - LyricTextLine.IndexOf('「') - 1);
                    lrctxt = lrctxt.Substring(0, lrctxt.IndexOf('「'));
                }

                bool HaveTranslation = !string.IsNullOrEmpty(translation);
                Lyrics.Add(new SongLyric()
                {
                    LyricTime = time,
                    PureLyric = lrctxt,
                    Translation = translation,
                    HaveTranslation = HaveTranslation
                });
            }
        }

        public static void AudioMediaPlaybackList_ItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
        {
            Common.BarPlayBar.OnSongAdd();
        }

        public static void AudioMediaPlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            LoadPureLyric(AudioInfos[args.NewItem].Lyric);
            Common.BarPlayBar.LoadPlayingFile(args.NewItem);
            Common.PageExpandedPlayer.OnSongChange(args.NewItem);
        }

        public static async void AppendFile(StorageFile sf)
        {
            var mediaPlaybackItem = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(sf));
            var afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            var properties = mediaPlaybackItem.GetDisplayProperties();
            AudioInfo ai = new AudioInfo()
            {
                Album = string.IsNullOrEmpty(afi.Tag.Album) ? "未知专辑" : afi.Tag.Album,
                Artist = string.IsNullOrEmpty(string.Join('/', afi.Tag.Performers)) ? "未知歌手" : string.Join('/', afi.Tag.Performers),
                LengthInMilliseconds = afi.Properties.Duration.Milliseconds,
                SongName = string.IsNullOrEmpty(afi.Tag.Title) ? "Untitled" : afi.Tag.Title
            };

            //记载歌词
            try
            {
                StorageFile lrcfile = await (await sf.GetParentAsync()).GetFileAsync(Path.ChangeExtension(sf.Name, "lrc"));
                ai.Lyric = await FileIO.ReadTextAsync(lrcfile);
            }
            catch (Exception) { }
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = ai.Album;
            properties.MusicProperties.Artist = ai.Artist;
            properties.MusicProperties.Title = ai.SongName;

            BitmapImage img = new BitmapImage();
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                stream.AsStreamForWrite().Write(afi.Tag.Pictures[0].Data.ToArray(), 0, afi.Tag.Pictures[0].Data.ToArray().Length);
                stream.Seek(0);
                img.SetSource(stream);
                properties.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream);
            }
            ai.Picture = img;
            AudioInfos[mediaPlaybackItem] = ai;
            
            //sampleFile.DeleteAsync();
            mediaPlaybackItem.ApplyDisplayProperties(properties);

            AudioMediaPlaybackList.Items.Add(mediaPlaybackItem);
        }
    }

    public struct SongLyric
    {
        public string PureLyric;
        public string Translation;
        public bool HaveTranslation;
        public TimeSpan LyricTime;

        public static SongLyric PureSong = new SongLyric()
            {HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "纯音乐 请欣赏"};
    }

    struct AudioInfo
    {
        public string SongName;
        public string Artist;
        public string Album;
        public string Lyric;
        public int LengthInMilliseconds;
        public BitmapImage Picture;
    }
}
