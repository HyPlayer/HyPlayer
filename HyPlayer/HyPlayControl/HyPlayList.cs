using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using TagLib;

namespace HyPlayer.HyPlayControl
{
    public static class HyPlayList
    {
        /*********        基本       ********/
        public static int NowPlaying = 0;
        public static bool isPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        public static HyPlayItem NowPlayingItem => List[NowPlaying];
        public static readonly List<HyPlayItem> List = new List<HyPlayItem>();
        public static readonly Dictionary<MediaPlaybackItem, int> MPIToIndex = new Dictionary<MediaPlaybackItem, int>();
        //public static Timer Timer = null;
        public static List<SongLyric> Lyrics = new List<SongLyric>();

        /********        事件        ********/
        public delegate void PlayItemChangeEvent(HyPlayItem playItem);
        public static event PlayItemChangeEvent OnPlayItemChange;
        public delegate void PlayItemAddEvent(HyPlayItem playItem);
        public static event PlayItemAddEvent OnPlayItemAdd;
        public delegate void PauseEvent();
        public static event PauseEvent OnPause;
        public delegate void PlayEvent();
        public static event PauseEvent OnPlay;
        public delegate void PlayPositionChangeEvent(TimeSpan Position);
        public static event PlayPositionChangeEvent OnPlayPositionChange;
        public delegate void VolumeChangeEvent(double newVolumn);
        public static event VolumeChangeEvent OnVolumeChange;
        public delegate void PlayListAddEvent(HyPlayItem playItem);
        public static event PlayListAddEvent OnPlayListAdd;
        public delegate void LyricLoadedEvent();
        public static event LyricLoadedEvent OnLyricLoaded;

        /********        API        ********/
        public static MediaPlayer Player;
        public static MediaPlaybackList PlaybackList;



        public static void InitializeHyPlaylist()
        {
            PlaybackList = new MediaPlaybackList() { AutoRepeatEnabled = true };
            PlaybackList.ItemOpened += AudioMediaPlaybackList_ItemOpened;
            PlaybackList.CurrentItemChanged += AudioMediaPlaybackList_CurrentItemChanged;
            Player = new MediaPlayer()
            {
                Source = PlaybackList,
                AutoPlay = true
            };
            Player.CurrentStateChanged += Player_CurrentStateChanged;
            Player.VolumeChanged += Player_VolumeChanged;
            Player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
        }

        private static void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            Invoke(() => OnPlayPositionChange?.Invoke(Player.PlaybackSession.Position));
        }

        private static void Player_VolumeChanged(MediaPlayer sender, object args)
        {
            Invoke(() => OnVolumeChange?.Invoke(Player.Volume));
        }

        private static void LoadSystemPlayBar(int index)
        {
            if (index >= List.Count) return;
            var hpi = List[index];
            var ai = hpi.AudioInfo;
            //然后设置播放相关属性
            var properties = PlaybackList.Items[index].GetDisplayProperties();
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = ai.Album;
            properties.MusicProperties.Artist = ai.Artist;
            properties.MusicProperties.Title = ai.SongName;

            try
            {
                if (hpi.isOnline)
                {
                    properties.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(hpi.NcPlayItem.Album.cover + "?param=" + StaticSource.PICSIZE_AUDIO_PLAYER_COVER));
                }
                else
                {
                    properties.Thumbnail = ai.Thumbnail;

                }
            }
            catch { }

            hpi.MediaItem.ApplyDisplayProperties(properties);
        }

        private static void Player_CurrentStateChanged(MediaPlayer sender, object args)
        {
            if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                Invoke(() => OnPlay?.Invoke());
            }
            else
            {
                Invoke(() => OnPause?.Invoke());
            }
        }

        private static void AudioMediaPlaybackList_ItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
        {
            Invoke(() => OnPlayItemAdd?.Invoke(List[MPIToIndex[args.Item]]));
        }

        public static async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        public static async void AudioMediaPlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            if (args.NewItem == null) return;
            NowPlaying = (int)HyPlayList.PlaybackList.CurrentItemIndex;
            HyPlayItem hpi = List[MPIToIndex[args.NewItem]];
            var ai = hpi.AudioInfo;
            //LoadSystemPlayBar(MPIToIndex[args.NewItem]);
            if (hpi.ItemType == HyPlayItemType.Netease && hpi.AudioInfo.Lyric == null)
            {
                var lrcs = await LoadNCLyric(hpi);
                ai.Lyric = lrcs.PureLyrics;
                ai.TrLyric = lrcs.TrLyrics;
            }
            //先进行歌词转换以免被搞
            Lyrics = Utils.ConvertPureLyric(ai.Lyric);
            Utils.ConvertTranslation(ai.TrLyric, Lyrics);
            hpi.AudioInfo = ai;
            //这里为调用订阅本事件的元素
            Invoke(() => OnPlayItemChange?.Invoke(hpi));
        }


        public static async Task<PureLyricInfo> LoadNCLyric(HyPlayItem ncp)
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                new Dictionary<string, object>() { { "id", ncp.NcPlayItem.sid } });
            if (isOk)
            {
                if (json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true")
                {
                    return new PureLyricInfo()
                    {
                        PureLyrics = null,
                        TrLyrics = null
                    };
                }
                else if (json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true")
                {
                    return new PureLyricInfo()
                    {
                        PureLyrics = "[00:00.000] 无歌词 请欣赏",
                        TrLyrics = null
                    };
                }
                else
                {
                    try
                    {
                        return new PureLyricInfo()
                        {
                            PureLyrics = json["lrc"]["lyric"].ToString(),
                            TrLyrics = json["tlyric"]["lyric"].ToString()
                        };
                    }
                    catch (Exception)
                    {
                        //DEBUG
                    }
                }
            }

            return new PureLyricInfo();
        }

        /********        播放文件相关        ********/

        public static async Task<HyPlayItem> AppendNCSong(NCSong ncSong)
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object>() { { "id", ncSong.sid }, { "br", 320000 } });
            if (isOk)
            {
                if (json["data"][0]["code"].ToString() != "200") return null; //未获取到
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
                return AppendNCPlayItem(ncp);
            }
            else
            {
                return null;
            }
        }

        public static HyPlayItem AppendNCPlayItem(NCPlayItem ncp)
        {
            AudioInfo ai = new AudioInfo()
            {
                Album = ncp.Album.name,
                Artist = string.Join(" / ", ncp.Artist.Select((artist => artist.name))),
                LengthInMilliseconds = ncp.LengthInMilliseconds,
                Picture = ncp.Album.cover,
                SongName = ncp.songname
            };
            var hpi = new HyPlayItem()
            {
                AudioInfo = ai,
                isOnline = true,
                ItemType = HyPlayItemType.Netease,
                Name = ncp.songname,
                NcPlayItem = ncp,
                Path = ncp.url
            };
            List.Add(hpi);
            SyncPlayList();
            return hpi;
        }

        public static async void SyncPlayList()
        {
            if (List.Count == 0)
            {
                PlaybackList.Items.Clear();
                MPIToIndex.Clear();
                return;
            }
            if (PlaybackList.Items.Count > List.Count)
            {
                MPIToIndex.Clear();
                for (int i = List.Count - 1; i < PlaybackList.Items.Count; i++)
                {
                    PlaybackList.Items.RemoveAt(i);
                }
            }
            for (int i = 0; i < List.Count; i++)
            {
                if (PlaybackList.Items.Count <= i || List[i].MediaItem == null)
                {
                    MediaPlaybackItem mediaPlaybackItem;
                    RandomAccessStreamReference rasr;
                    if (List[i].ItemType == HyPlayItemType.Netease)
                    {
                        try
                        {
                            StorageFile item =
                                await Windows.Storage.ApplicationData.Current.LocalCacheFolder.GetFileAsync(
                                    "SongCache\\" + List[i].NcPlayItem.sid +
                                    "." + List[i].NcPlayItem.subext.ToLower());
                            if (List[i].NcPlayItem.size == (await item.GetBasicPropertiesAsync()).Size.ToString())
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

                            mediaPlaybackItem = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(List[i].NcPlayItem.url)));
                        }
                        rasr = RandomAccessStreamReference.CreateFromUri(new Uri(List[i].NcPlayItem.Album.cover+"?param="+StaticSource.PICSIZE_AUDIO_PLAYER_COVER));
                        List[i].MediaItem = mediaPlaybackItem;
                    }
                    else
                    {
                        mediaPlaybackItem = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(List[i].AudioInfo.LocalSongFile));
                        rasr = List[i].AudioInfo.Thumbnail;
                    }

                    var properties = mediaPlaybackItem.GetDisplayProperties();
                    properties.Type = MediaPlaybackType.Music;
                    properties.MusicProperties.AlbumTitle = List[i].AudioInfo.Album;
                    properties.MusicProperties.Artist = List[i].AudioInfo.Artist;
                    properties.MusicProperties.Title = List[i].AudioInfo.SongName;
                    properties.Thumbnail = rasr;
                    List[i].MediaItem = mediaPlaybackItem;

                    List[i].MediaItem.ApplyDisplayProperties(properties);
                    MPIToIndex[List[i].MediaItem] = i;
                    PlaybackList.Items.Add(List[i].MediaItem);
                    Invoke(() =>
                    {
                        if (i >= 0 && List.Count > i) OnPlayListAdd?.Invoke(List[i]);
                    });
                }
                if (i >= 0 && List.Count > i && List[i].MediaItem != PlaybackList.Items[i])
                    PlaybackList.Items[i] = List[i].MediaItem;
                MPIToIndex[List[i].MediaItem] = i;
            }
        }

        public static async void AppendFile(StorageFile sf)
        {
            var afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            AudioInfo ai = new AudioInfo()
            {
                Album = string.IsNullOrEmpty(afi.Tag.Album) ? "未知专辑" : afi.Tag.Album,
                Artist = string.IsNullOrEmpty(string.Join('/', afi.Tag.Performers)) ? "未知歌手" : string.Join('/', afi.Tag.Performers),
                LengthInMilliseconds = afi.Properties.Duration.TotalMilliseconds,
                SongName = string.IsNullOrEmpty(afi.Tag.Title) ? "Untitled" : afi.Tag.Title,
                LocalSongFile = sf
            };

            //记载歌词
            try
            {
                StorageFile lrcfile = await (await sf.GetParentAsync()).GetFileAsync(Path.ChangeExtension(sf.Name, "lrc"));
                ai.Lyric = await FileIO.ReadTextAsync(lrcfile);
            }
            catch (Exception) { }
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
                    ai.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(streamb);
                }

                ai.BitmapImage = img;
            }
            catch (Exception) { }

            var hyPlayItem = new HyPlayItem()
            {
                AudioInfo = ai,
                isOnline = false,
                ItemType = HyPlayItemType.Local,
                Name = ai.SongName,
                Path = sf.Path
            };

            List.Add(hyPlayItem);
            SyncPlayList();
        }
    }



    public static class Utils
    {
        public static List<SongLyric> ConvertPureLyric(string LyricAllText)
        {
            var Lyrics = new List<SongLyric>();
            if (string.IsNullOrEmpty(LyricAllText)) return new List<SongLyric>();
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
                while (lrctxt.Trim().StartsWith('['))
                {
                    //一句双时间
                    Lyrics = Lyrics.Union(ConvertPureLyric(lrctxt)).ToList();
                    lrctxt = lrctxt.Substring(LyricTextLine.IndexOf(']') + 1);
                }
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
            return Lyrics.OrderBy((lyric => lyric.LyricTime.TotalMilliseconds)).ToList();
        }

        public static void ConvertTranslation(string LyricAllText, List<SongLyric> Lyrics)
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
                while (lrctxt.Trim().StartsWith('['))
                {
                    //一句双时间
                    ConvertTranslation(lrctxt,Lyrics);
                }
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
    }

    public class UwpStorageFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly StorageFile file;

        public string Name => file.Name;

        public Stream ReadStream => file.OpenStreamForReadAsync().GetAwaiter().GetResult();

        public Stream WriteStream => file.OpenStreamForWriteAsync().GetAwaiter().GetResult();


        public UwpStorageFileAbstraction(StorageFile file)
        {
            this.file = file ?? throw new ArgumentNullException(nameof(file));
        }


        public void CloseStream(Stream stream)
        {
            stream?.Dispose();
        }
    }
}
