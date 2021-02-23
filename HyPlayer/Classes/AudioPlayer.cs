using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using NeteaseCloudMusicApi;
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
    static class AudioPlayer
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
                    LyricTime = time + offset,
                    PureLyric = lrctxt,
                    Translation = translation,
                    HaveTranslation = HaveTranslation
                });
            }
        }

        public static void LoadTranslation(string LyricAllText)
        {
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
                for (int i = 0; i < Lyrics.Count; i++)
                {
                    var songLyric = Lyrics[i];
                    if (songLyric.LyricTime == time)
                    {
                        songLyric.Translation = lrctxt;
                        songLyric.HaveTranslation = true;
                        Lyrics[i] = songLyric;
                    }
                }
            }
        }

        public static void AudioMediaPlaybackList_ItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
        {
            Common.BarPlayBar.OnSongAdd();
        }

        public static void AudioMediaPlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            LoadPureLyric(AudioInfos[args.NewItem].Lyric);
            LoadTranslation(AudioInfos[args.NewItem].TrLyric);
            Common.BarPlayBar.LoadPlayingFile(args.NewItem);
            Common.PageExpandedPlayer.OnSongChange(args.NewItem);
        }

        public static async void AppendNCSong(NCSong ncSong)
        {

            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object>() { { "id", ncSong.sid }, { "br", 320000 } });
            if (isOk)
            {
                if (json["data"][0]["code"].ToString() != "200") return; //未获取到
                NCPlayItem ncp = new NCPlayItem()
                {
                    Album = ncSong.Album,
                    Artist = ncSong.Artist,
                    subext = json["data"][0]["type"].ToString(),
                    sid = ncSong.sid,
                    songname = ncSong.songname,
                    url = json["data"][0]["url"].ToString(),
                    LengthInMilliseconds = ncSong.LengthInMilliseconds,
                    size = json["data"][0]["size"].ToString(),
                    md5 = json["data"][0]["md5"].ToString()
                };
                AppendNCPlayItem(ncp);
            }
        }

        public static async void AppendNCPlayItem(NCPlayItem ncp)
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                new Dictionary<string, object>() { { "id", ncp.sid } });
            if (isOk)
            {
                if (json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true")
                {
                    //纯音乐 无歌词
                    AppendNCPlayItem(ncp, null, null);
                }
                else if (json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true")
                {
                    AppendNCPlayItem(ncp, "[00:00.000] 该歌词未被网易云音乐收录", null);
                }
                else
                {
                    try
                    {
                        AppendNCPlayItem(ncp, json["lrc"]["lyric"].ToString(), json["tlyric"]["lyric"].ToString());
                    }
                    catch (Exception)
                    {
                        //DEBUG
                    }
                }
            }
        }

        public static async void AppendNCPlayItem(NCPlayItem ncp, string lyric, string translation)
        {
            MediaPlaybackItem mediaPlaybackItem;
            try
            {
                StorageFile item = await Windows.Storage.ApplicationData.Current.LocalCacheFolder.GetFileAsync("SongCache\\" + ncp.sid +
                    "." + ncp.subext.ToLower());
                if (ncp.size == (await item.GetBasicPropertiesAsync()).Size.ToString())
                {
                    mediaPlaybackItem = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(item));
                }
                else
                {
                    throw new Exception("文件大小不一致");
                }
            }
            catch (Exception)
            {

                mediaPlaybackItem = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(ncp.url)));
            }

            AudioInfo ai = new AudioInfo()
            {
                Album = ncp.Album.name,
                Artist = string.Join(" / ", ncp.Artist.Select((artist => artist.name))),
                LengthInMilliseconds = ncp.LengthInMilliseconds,
                Lyric = lyric,
                Picture = new BitmapImage(new Uri(ncp.Album.cover)),
                SongName = ncp.songname,
                TrLyric = translation
            };
            var properties = mediaPlaybackItem.GetDisplayProperties();
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = ai.Album;
            properties.MusicProperties.Artist = ai.Artist;
            properties.MusicProperties.Title = ai.SongName;
            try
            {
                properties.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(ncp.Album.cover + "?param="+StaticSource.PICSIZE_AUDIO_PLAYER_COVER));
            }
            catch (Exception) { }
            mediaPlaybackItem.ApplyDisplayProperties(properties);
            AudioInfos[mediaPlaybackItem] = ai;
            AudioMediaPlaybackList.Items.Add(mediaPlaybackItem);
            Common.BarPlayBar.RefreshSongList();
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
                LengthInMilliseconds = afi.Properties.Duration.TotalMilliseconds,
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
            try
            {
                BitmapImage img = new BitmapImage();
                using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
                {
                    stream.AsStreamForWrite().Write(afi.Tag.Pictures[0].Data.ToArray(), 0,
                        afi.Tag.Pictures[0].Data.ToArray().Length);
                    stream.Seek(0);
                    await img.SetSourceAsync(stream);
                    InMemoryRandomAccessStream streamb = new InMemoryRandomAccessStream();
                    streamb.AsStreamForWrite().Write(afi.Tag.Pictures[0].Data.ToArray(), 0,
                        afi.Tag.Pictures[0].Data.ToArray().Length);
                    streamb.Seek(0);
                    properties.Thumbnail =
                        Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(streamb);
                }

                ai.Picture = img;
            }
            catch (Exception) { }

            AudioInfos[mediaPlaybackItem] = ai;

            //sampleFile.DeleteAsync();
            mediaPlaybackItem.ApplyDisplayProperties(properties);

            AudioMediaPlaybackList.Items.Add(mediaPlaybackItem);
            Common.BarPlayBar.RefreshSongList();
        }
    }

    public struct SongLyric
    {
        public string PureLyric;
        public string Translation;
        public bool HaveTranslation;
        public TimeSpan LyricTime;

        public static SongLyric PureSong = new SongLyric()
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "纯音乐 请欣赏" };
    }

    struct AudioInfo
    {
        public string SongName;
        public string Artist;
        public string Album;
        public string Lyric;
        public string TrLyric;
        public double LengthInMilliseconds;
        public BitmapImage Picture;
    }
}
