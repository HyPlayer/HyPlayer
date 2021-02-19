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
using Windows.UI.Xaml.Media.Imaging;
using TagLib;

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
        public static Dictionary<MediaPlaybackItem, AudioInfo> AudioInfos = new Dictionary<MediaPlaybackItem, AudioInfo>();
        public static Random AudioRandom = new Random();

        public static void AudioMediaPlaybackList_ItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
        {
            Common.BarPlayBar.OnSongAdd();
        }

        public static void AudioMediaPlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            Common.BarPlayBar.LoadPlayingFile(args.NewItem);
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
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = ai.Album;
            properties.MusicProperties.Artist = ai.Artist;
            properties.MusicProperties.Title = ai.SongName;
            /*
                MemoryStream st = new MemoryStream(afi.Tag.Pictures[0].Data.ToArray());
                IRandomAccessStream ras = st.AsRandomAccessStream();
                BitmapImage bi = new BitmapImage();
            */
            Windows.Storage.StorageFolder storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile sampleFile =
                await storageFolder.CreateFileAsync("ImgCache\\Albums\\" + AudioRandom.Next().ToString(),
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);
            sampleFile.OpenStreamForWriteAsync().Result.Write(afi.Tag.Pictures[0].Data.ToArray(), 0, afi.Tag.Pictures[0].Data.ToArray().Length);
            ai.Picture = new BitmapImage(new Uri(sampleFile.Path));
            AudioInfos[mediaPlaybackItem] = ai;
            properties.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(sampleFile);
            //sampleFile.DeleteAsync();
            mediaPlaybackItem.ApplyDisplayProperties(properties);

            AudioMediaPlaybackList.Items.Add(mediaPlaybackItem);
        }
    }

    struct AudioInfo
    {
        public string SongName;
        public string Artist;
        public string Album;
        public int LengthInMilliseconds;
        public BitmapImage Picture;
    }
}
