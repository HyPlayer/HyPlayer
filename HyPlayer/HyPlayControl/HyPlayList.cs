using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.HyPlayControl
{
    public static class HyPlayList
    {
        /*********        基本       ********/
        public static PlayMode NowPlayType = PlayMode.DefaultRoll;
        public static int NowPlaying = 0;
        public static bool isPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        public static HyPlayItem NowPlayingItem => List[NowPlaying];
        public static readonly List<HyPlayItem> List = new List<HyPlayItem>();
        public static List<SongLyric> Lyrics = new List<SongLyric>();


        /********        事件        ********/
        public delegate void PlayItemChangeEvent(HyPlayItem playItem);
        public static event PlayItemChangeEvent OnPlayItemChange;
        public delegate void PlayItemAddEvent(HyPlayItem playItem);
        public static event PlayItemAddEvent OnPlayItemAdd;
        public delegate void PauseEvent();
        public static event PauseEvent OnPause;
        public delegate void PlayEvent();
        public static event PlayEvent OnPlay;
        public delegate void PlayPositionChangeEvent(TimeSpan Position);
        public static event PlayPositionChangeEvent OnPlayPositionChange;
        public delegate void VolumeChangeEvent(double newVolumn);
        public static event VolumeChangeEvent OnVolumeChange;
        public delegate void PlayListAddDoneEvent();
        public static event PlayListAddDoneEvent OnPlayListAddDone;
        public delegate void LyricLoadedEvent();
        public static event LyricLoadedEvent OnLyricLoaded;
        public delegate void LyricChangeEvent(SongLyric lrc);
        public static event LyricChangeEvent OnLyricChange;
        public delegate void MediaEndEvent(HyPlayItem hpi);
        public static event MediaEndEvent OnMediaEnd;

        /********        API        ********/
        public static MediaPlayer Player;
        public static SystemMediaTransportControls MediaSystemControls;
        public static SystemMediaTransportControlsDisplayUpdater ControlsDisplayUpdater;
        public static BackgroundDownloader downloader = new BackgroundDownloader();



        public static void InitializeHyPlaylist()
        {
            Player = new MediaPlayer()
            {
                AutoPlay = true,
                IsLoopingEnabled = false
            };
            MediaSystemControls = SystemMediaTransportControls.GetForCurrentView();
            ControlsDisplayUpdater = MediaSystemControls.DisplayUpdater;
            Player.CommandManager.IsEnabled = false;
            MediaSystemControls.IsPlayEnabled = true;
            MediaSystemControls.IsPauseEnabled = true;
            MediaSystemControls.IsNextEnabled = true;
            MediaSystemControls.IsPreviousEnabled = true;
            MediaSystemControls.IsEnabled = false;
            MediaSystemControls.ButtonPressed += SystemControls_ButtonPressed;
            MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
            Player.SourceChanged += Player_SourceChanged;
            Player.MediaEnded += Player_MediaEnded;
            Player.CurrentStateChanged += Player_CurrentStateChanged;
            Player.VolumeChanged += Player_VolumeChanged;
            Player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
        }

        /********        方法         ********/
        public static void SongAppendDone()
        {
            Invoke(() => OnPlayListAddDone?.Invoke());
        }

        public static void SongMoveNext()
        {
            MoveSongPointer(true);
            LoadPlayerSong();
            Player.Play();
        }

        public static void SongMovePrevious()
        {
            if (NowPlaying - 1 < 0)
            {
                NowPlaying = List.Count - 1;
            }
            else
            {
                NowPlaying--;
            }
            LoadPlayerSong();
            Player.Play();
        }

        public static void SongMoveTo(int index)
        {
            if (List.Count <= index) return;
            NowPlaying = index;
            LoadPlayerSong();
            Player.Play();
        }

        public static void RemoveSong(int index)
        {
            if (List.Count <= index) return;
            List.RemoveAt(index);
            LoadPlayerSong();
        }

        public static void RemoveAllSong()
        {
            List.Clear();
            Player.Source = null;
        }

        /********        相关事件处理        ********/

        private static void SystemControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Invoke(() => Player.Play());
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Invoke(() => Player.Pause());
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Invoke(() => SongMovePrevious());
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Invoke(() => SongMoveNext());
                    break;
                default:
                    break;
            }
        }

        private static void MoveSongPointer(bool realnext = false)
        {
            //首先切换指针到下一首要播放的歌
            switch (NowPlayType)
            {
                case PlayMode.DefaultRoll:
                    //正常Roll的话,id++
                    if (NowPlaying + 1 >= List.Count)
                    {
                        NowPlaying = 0;
                    }
                    else
                    {
                        NowPlaying++;
                    }

                    break;
                case PlayMode.Shuffled:
                    //随机播放
                    NowPlaying = new Random().Next(List.Count - 1);
                    break;
                case PlayMode.SinglePlay:
                    if (realnext)
                    {
                        if (NowPlaying + 1 >= List.Count)
                        {
                            NowPlaying = 0;
                        }
                        else
                        {

                            NowPlaying++;
                        }
                    }
                    break;
            }
        }

        private static void Player_MediaEnded(MediaPlayer sender, object args)
        {
            //当播放结束时,此时你应当进行切歌操作
            //不过在此之前还是把订阅了的时间给返回回去吧
            Invoke(() => OnMediaEnd?.Invoke(NowPlayingItem));
            MoveSongPointer();
            //然后尝试加载下一首歌
            LoadPlayerSong();
        }

        private static async void LoadPlayerSong()
        {
            MediaSource ms;
            if (NowPlayingItem.isOnline)
            {
                //检测是否已经缓存且大小正常
                try
                {
                    StorageFile sf =
                        await ApplicationData.Current.LocalCacheFolder.GetFileAsync(NowPlayingItem.NcPlayItem.sid +
                            "." + NowPlayingItem.NcPlayItem.subext);
                    if ((await sf.GetBasicPropertiesAsync()).Size.ToString() == NowPlayingItem.NcPlayItem.size)
                    {
                        ms = MediaSource.CreateFromStorageFile(sf);
                    }
                    else
                    {
                        throw new Exception("文件大小不匹配");
                    }
                }
                catch (Exception)
                {
                    //尝试从DownloadOperation下载
                    /*
                    StorageFile destinationFile = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync(NowPlayingItem.NcPlayItem.sid +
                        "." + NowPlayingItem.NcPlayItem.subext, CreationCollisionOption.ReplaceExisting);
                    var downloadOperation = downloader.CreateDownload(new Uri(NowPlayingItem.NcPlayItem.url), destinationFile);
                    downloadOperation.IsRandomAccessRequired = true;
                    var startAsyncTask = downloadOperation.StartAsync().AsTask();
                    
                    */
                    ms =
                        MediaSource.CreateFromUri(new Uri(NowPlayingItem.NcPlayItem.url));
                }
            }
            else
            {
                ms = MediaSource.CreateFromStorageFile(NowPlayingItem.AudioInfo.LocalSongFile);
            }
            Player.Source = ms;
            MediaSystemControls.IsEnabled = true;
            Player.Play();
        }

        private static async void Player_SourceChanged(MediaPlayer sender, object args)
        {
            if (List.Count <= NowPlaying) return;
            //当加载一个新的播放文件时,此时你应当加载歌词和SMTC
            //加载SMTC
            ControlsDisplayUpdater.Type = MediaPlaybackType.Music;
            ControlsDisplayUpdater.MusicProperties.Artist = NowPlayingItem.AudioInfo.Artist;
            ControlsDisplayUpdater.MusicProperties.AlbumTitle = NowPlayingItem.AudioInfo.Album;
            ControlsDisplayUpdater.MusicProperties.Title = NowPlayingItem.AudioInfo.SongName;
            //因为加载图片可能会高耗时,所以在此处加载
            Invoke(() => OnPlayItemChange?.Invoke(NowPlayingItem));
            //加载歌词
            LoadLyrics(NowPlayingItem);
            ControlsDisplayUpdater.Thumbnail = NowPlayingItem.isOnline ? RandomAccessStreamReference.CreateFromUri(new Uri(NowPlayingItem.NcPlayItem.Album.cover)) : RandomAccessStreamReference.CreateFromStream(await NowPlayingItem.AudioInfo.LocalSongFile.GetThumbnailAsync(ThumbnailMode.MusicView, 9999));
            ControlsDisplayUpdater.Update();
        }

        private static void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            Invoke(() => OnPlayPositionChange?.Invoke(Player.PlaybackSession.Position));
            LoadLyricChange();
        }

        private static void LoadLyricChange()
        {
            SongLyric songLyric = Lyrics.LastOrDefault((t => t.LyricTime < Player.PlaybackSession.Position));
            Invoke(() => OnLyricChange?.Invoke(songLyric));
        }

        private static void Player_VolumeChanged(MediaPlayer sender, object args)
        {
            Invoke(() => OnVolumeChange?.Invoke(Player.Volume));
        }

        private static void Player_CurrentStateChanged(MediaPlayer sender, object args)
        {
            //先通知SMTC
            switch (Player.PlaybackSession.PlaybackState)
            {
                case MediaPlaybackState.Playing:
                    MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    break;
                case MediaPlaybackState.Paused:
                    MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
                default:
                    break;
            }

            if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                Invoke(() => OnPlay?.Invoke());
            }
            else
            {
                Invoke(() => OnPause?.Invoke());
            }
        }

        public static async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        public static async void LoadLyrics(HyPlayItem hpi)
        {
            if (hpi.ItemType == HyPlayItemType.Netease && hpi.AudioInfo.Lyric == null)
            {
                PureLyricInfo lrcs = await LoadNCLyric(hpi);
                hpi.AudioInfo.Lyric = lrcs.PureLyrics;
                hpi.AudioInfo.TrLyric = lrcs.TrLyrics;
            }
            //先进行歌词转换以免被搞
            Lyrics = Utils.ConvertPureLyric(hpi.AudioInfo.Lyric);
            Utils.ConvertTranslation(hpi.AudioInfo.TrLyric, Lyrics);
            Invoke(() => OnLyricLoaded?.Invoke());
        }
        public static async Task<PureLyricInfo> LoadNCLyric(HyPlayItem ncp)
        {
            (bool isOk, Newtonsoft.Json.Linq.JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
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
            (bool isOk, Newtonsoft.Json.Linq.JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object>() { { "id", ncSong.sid }/*, { "br", 320000 } */});
            if (isOk)
            {
                try
                {
                    if (json["data"][0]["code"].ToString() != "200")
                    {
                        return null; //未获取到
                    }

                    string tag = "";
                    if (json["data"][0]["type"].ToString().ToLowerInvariant() == "flac")
                    {
                        tag = "SQ";
                    }
                    else
                    {
                        tag = (json["data"][0]["br"].ToObject<int>() / 1000).ToString()+ "k";
                    }

                    NCPlayItem ncp = new NCPlayItem()
                    {
                        tag = tag,
                        Album = ncSong.Album,
                        Artist = ncSong.Artist,
                        subext = json["data"][0]["type"].ToString().ToLowerInvariant(),
                        sid = ncSong.sid,
                        songname = ncSong.songname,
                        url = json["data"][0]["url"].ToString(),
                        LengthInMilliseconds = ncSong.LengthInMilliseconds,
                        size = json["data"][0]["size"].ToString(),
                        md5 = json["data"][0]["md5"].ToString()
                    };
                    return AppendNCPlayItem(ncp);
                }
                catch
                {
                    return null;
                }

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
                ArtistArr = ncp.Artist.Select((artist => artist.name)).ToArray(),
                Artist = string.Join(" / ", ncp.Artist.Select((artist => artist.name))),
                LengthInMilliseconds = ncp.LengthInMilliseconds,
                Picture = ncp.Album.cover,
                SongName = ncp.songname,
                tag=ncp.tag
            };
            HyPlayItem hpi = new HyPlayItem()
            {
                AudioInfo = ai,
                isOnline = true,
                ItemType = HyPlayItemType.Netease,
                Name = ncp.songname,
                NcPlayItem = ncp,
                Path = ncp.url
            };
            List.Add(hpi);
            return hpi;
        }

        public static async Task<bool> AppendFile(StorageFile sf)
        {
            //TagLib.File afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            var mdp = await sf.Properties.GetMusicPropertiesAsync();
            string[] contributingArtistsKey = { "System.Music.Artist" };
            IDictionary<string, object> contributingArtistsProperty =
                await mdp.RetrievePropertiesAsync(contributingArtistsKey);
            string[] contributingArtists = contributingArtistsProperty["System.Music.Artist"] as string[];
            AudioInfo ai = new AudioInfo()
            {
                tag = "本地",
                Album = string.IsNullOrEmpty(mdp.Album) ? "未知专辑" : mdp.Album,
                ArtistArr = contributingArtists,
                Artist = string.IsNullOrEmpty(string.Join('/', contributingArtists)) ? "未知歌手" : string.Join('/', contributingArtists),
                LengthInMilliseconds = mdp.Duration.TotalMilliseconds,
                SongName = string.IsNullOrEmpty(mdp.Title) ? sf.DisplayName : mdp.Title,
                LocalSongFile = sf
            };

            //记载歌词
            try
            {
                StorageFile lrcfile = await (await sf.GetParentAsync()).GetFileAsync(Path.ChangeExtension(sf.Name, "lrc"));
                ai.Lyric = await FileIO.ReadTextAsync(lrcfile);
            }
            catch (Exception) { }

            HyPlayItem hyPlayItem = new HyPlayItem()
            {
                AudioInfo = ai,
                isOnline = false,
                ItemType = HyPlayItemType.Local,
                Name = ai.SongName,
                Path = sf.Path
            };

            List.Add(hyPlayItem);
            return true;

        }
    }

    public enum PlayMode
    {
        DefaultRoll,
        SinglePlay,
        Shuffled
    }

    public static class Utils
    {
        public static List<SongLyric> ConvertPureLyric(string LyricAllText)
        {
            List<SongLyric> Lyrics = new List<SongLyric>();
            if (string.IsNullOrEmpty(LyricAllText))
            {
                return new List<SongLyric>();
            }

            string[] LyricsArr = LyricAllText.Replace("\r\n", "\n").Replace("\r", "\n").Split("\n");
            TimeSpan offset = TimeSpan.Zero;
            foreach (string sL in LyricsArr)
            {
                string LyricTextLine = sL.Trim();
                if (LyricTextLine.IndexOf('[') == -1 || LyricTextLine.IndexOf(']') == -1)
                {
                    continue; //此行不为Lrc
                }

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
                    {
                        continue;
                    }

                    offset = new TimeSpan(0, 0, 0, 0, offsetint);
                }

                if (!TimeSpan.TryParse("00:" + prefix, out TimeSpan time))
                {
                    continue;
                }

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
            if (string.IsNullOrEmpty(LyricAllText))
            {
                return;
            }

            string[] LyricsArr = LyricAllText.Replace("\r\n", "\n").Replace("\r", "\n").Split("\n");
            TimeSpan offset = TimeSpan.Zero;
            foreach (string sL in LyricsArr)
            {
                string LyricTextLine = sL.Trim();
                if (LyricTextLine.IndexOf('[') == -1 || LyricTextLine.IndexOf(']') == -1)
                {
                    continue; //此行不为Lrc
                }

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
                    {
                        continue;
                    }

                    offset = new TimeSpan(0, 0, 0, 0, offsetint);
                }

                if (!TimeSpan.TryParse("00:" + prefix, out TimeSpan time))
                {
                    continue;
                }

                string lrctxt = LyricTextLine.Substring(LyricTextLine.IndexOf(']') + 1);
                while (lrctxt.Trim().StartsWith('['))
                {
                    //一句双时间
                    ConvertTranslation(lrctxt, Lyrics);
                }
                for (int i = 0; i < Lyrics.Count; i++)
                {
                    SongLyric songLyric = Lyrics[i];
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
}
