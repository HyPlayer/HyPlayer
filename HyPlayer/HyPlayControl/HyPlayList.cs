using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using File = TagLib.File;

namespace HyPlayer.HyPlayControl
{
    public static class HyPlayList
    {
        private static int GCCountDown = 10;

        public delegate void TimerTicked();

        public delegate void LoginDoneEvent();

        public delegate void LyricChangeEvent();

        public delegate void LyricLoadedEvent();

        public delegate void MediaEndEvent(HyPlayItem hpi);

        //public delegate void PlayItemAddEvent(HyPlayItem playItem);
        //public static event PlayItemAddEvent OnPlayItemAdd; //此方法因为效率原因废弃
        public delegate void PauseEvent();

        public delegate void PlayEvent();


        /********        事件        ********/
        public delegate void PlayItemChangeEvent(HyPlayItem playItem);

        public delegate void PlayListAddDoneEvent();

        public delegate void PlayPositionChangeEvent(TimeSpan Position);

        public delegate void SongBufferEndEvent();

        public delegate void SongBufferStartEvent();

        public delegate void SongMoveNextEvent();

        public delegate void VolumeChangeEvent(double newVolumn);

        /*********        基本       ********/
        public static PlayMode NowPlayType = PlayMode.DefaultRoll;
        public static int NowPlaying;
        public static Timer SecTimer = new Timer(1000); // 公用秒表
        public static readonly List<HyPlayItem> List = new List<HyPlayItem>();
        public static List<SongLyric> Lyrics = new List<SongLyric>();

        /********        API        ********/
        public static MediaPlayer Player;
        public static SystemMediaTransportControls MediaSystemControls;
        public static SystemMediaTransportControlsDisplayUpdater ControlsDisplayUpdater;
        public static BackgroundDownloader downloader = new BackgroundDownloader();

        public static int lyricpos;
        public static int crashedTime;
        public static bool isPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        public static HyPlayItem NowPlayingItem
        {
            get
            {
                if (List.Count <= NowPlaying) return new HyPlayItem { ItemType = HyPlayItemType.Netease };
                return List[NowPlaying];
            }
        }

        public static event PlayItemChangeEvent OnPlayItemChange;

        public static event PauseEvent OnPause;

        public static event PlayEvent OnPlay;

        public static event PlayPositionChangeEvent OnPlayPositionChange;

        public static event VolumeChangeEvent OnVolumeChange;

        public static event PlayListAddDoneEvent OnPlayListAddDone;

        public static event LyricLoadedEvent OnLyricLoaded;

        public static event LyricChangeEvent OnLyricChange;

        public static event MediaEndEvent OnMediaEnd;

        public static event LyricChangeEvent OnSongMoveNext;

        public static event LyricChangeEvent OnSongBufferStart;

        public static event LyricChangeEvent OnSongBufferEnd;

        public static event LoginDoneEvent OnLoginDone;

        public static event TimerTicked OnTimerTicked;

        public static void InitializeHyPlaylist()
        {
            Player = new MediaPlayer
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
            MediaSystemControls.PlaybackPositionChangeRequested += MediaSystemControls_PlaybackPositionChangeRequested;
            //Player.SourceChanged += Player_SourceChanged;   //锚点修改
            Player.MediaEnded += Player_MediaEnded;
            Player.CurrentStateChanged += Player_CurrentStateChanged;
            Player.VolumeChanged += Player_VolumeChanged;
            Player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
            Player.MediaFailed += PlayerOnMediaFailed;
            Player.BufferingStarted += Player_BufferingStarted;
            Player.BufferingEnded += Player_BufferingEnded;
            SecTimer.Elapsed += (sender, args) => Common.Invoke(() => OnTimerTicked?.Invoke());
            SecTimer.Start();
            OnTimerTicked += () =>
            {
                if (GCCountDown-- < 0)
                {
                    GCCountDown = 10;
                    GC.Collect(0,GCCollectionMode.Forced);
                }
            };
            HistoryManagement.InitializeHistoryTrack();
            Common.Invoke(async () =>
            {
                foreach (NCSong song in await HistoryManagement.GetNCSongHistory())
                {
                    await HyPlayList.AppendNCSong(song);
                }
            });
            Common.GLOBAL["PERSONALFM"] = "false";
        }


        public static void LoginDownCall()
        {
            Common.Invoke(() => { OnLoginDone?.Invoke(); });
        }

        private static void MediaSystemControls_PlaybackPositionChangeRequested(SystemMediaTransportControls sender,
            PlaybackPositionChangeRequestedEventArgs args)
        {
            Player.PlaybackSession.Position = args.RequestedPlaybackPosition;
        }

        private static void Player_BufferingEnded(MediaPlayer sender, object args)
        {
            Common.Invoke(() => OnSongBufferEnd?.Invoke());
        }

        private static void Player_BufferingStarted(MediaPlayer sender, object args)
        {
            Common.Invoke(() => OnSongBufferStart?.Invoke());
        }

        private static void PlayerOnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            //歌曲崩溃了的话就是这个
            //SongMoveNext();
            //TimeSpan temppos = Player.PlaybackSession.Position;
            if (crashedTime == NowPlayingItem.GetHashCode())
            {
                SongMoveNext();
                crashedTime = 0;
            }
            else
            {
                crashedTime = NowPlayingItem.GetHashCode();
                if (NowPlayingItem.ItemType == HyPlayItemType.Netease ||
                    NowPlayingItem.ItemType == HyPlayItemType.Radio)
                    //TODO FM和普通歌曲一起
                    Common.Invoke(async () =>
                    {
                        List[NowPlaying] = await LoadNCSong(new NCSong
                        {
                            Type = NowPlayingItem.ItemType,
                            Album = NowPlayingItem.NcPlayItem.Album,
                            Artist = NowPlayingItem.NcPlayItem.Artist,
                            LengthInMilliseconds = NowPlayingItem.NcPlayItem.LengthInMilliseconds,
                            sid = NowPlayingItem.NcPlayItem.id,
                            songname = NowPlayingItem.NcPlayItem.songname
                        });
                        LoadPlayerSong();
                    });
                else
                    //本地歌曲炸了的话就Move下一首吧
                    SongMoveNext();
            }

            //Player.PlaybackSession.Position = temppos;
        }

        /********        方法         ********/
        public static void SongAppendDone()
        {
            Common.Invoke(() => OnPlayListAddDone?.Invoke());
        }

        public static void SongMoveNext()
        {
            Common.Invoke(() => OnSongMoveNext?.Invoke());
            if (List.Count == 0) return;
            MoveSongPointer(true);
            LoadPlayerSong();
        }

        public static void SongMovePrevious()
        {
            if (List.Count == 0) return;
            if (NowPlaying - 1 < 0)
                NowPlaying = List.Count - 1;
            else
                NowPlaying--;

            LoadPlayerSong();
        }

        public static void SongMoveTo(int index)
        {
            if (List.Count <= index) return;
            NowPlaying = index;
            LoadPlayerSong();
        }

        public static void RemoveSong(int index)
        {
            if (List.Count <= index) return;
            if (List.Count - 1 == 0)
            {
                RemoveAllSong();
                return;
            }

            if (index == NowPlaying)
            {
                List.RemoveAt(index);
                LoadPlayerSong();
            }

            if (index < NowPlaying)
            {
                //需要将序号向前挪动
                NowPlaying--;
                List.RemoveAt(index);
            }

            if (index > NowPlaying)
                List.RemoveAt(index);
            //假如移除后面的我就不管了
        }

        public static void RemoveAllSong()
        {
            List.Clear();
            Player.Source = null;
        }

        /********        相关事件处理        ********/

        private static void SystemControls_ButtonPressed(SystemMediaTransportControls sender,
            SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Common.Invoke(() => Player.Play());
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Common.Invoke(() => Player.Pause());
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Common.Invoke(() => SongMovePrevious());
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Common.Invoke(() => SongMoveNext());
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
                        NowPlaying = 0;
                    else
                        NowPlaying++;

                    break;
                case PlayMode.Shuffled:
                    //随机播放
                    NowPlaying = new Random().Next(List.Count - 1);
                    break;
                case PlayMode.SinglePlay:
                    if (realnext)
                    {
                        if (NowPlaying + 1 >= List.Count)
                            NowPlaying = 0;
                        else
                            NowPlaying++;
                    }

                    break;
            }
        }

        private static void Player_MediaEnded(MediaPlayer sender, object args)
        {
            //当播放结束时,此时你应当进行切歌操作
            //不过在此之前还是把订阅了的时间给返回回去吧
            Common.Invoke(() => OnMediaEnd?.Invoke(NowPlayingItem));
            MoveSongPointer();
            //然后尝试加载下一首歌
            LoadPlayerSong();
        }

        private static async void LoadPlayerSong()
        {
            if (NowPlayingItem == null)
            {
                MoveSongPointer();
            }
            Player_SourceChanged(null, null);
            MediaSource ms;
            switch (NowPlayingItem.ItemType)
            {
                case HyPlayItemType.Netease:
                case HyPlayItemType.Radio: //TODO FM伪加载为普通歌曲
                    //检测是否已经缓存且大小正常
                    try
                    {
                        throw new Exception("NOCACHE");
                        var sf =
                            await ApplicationData.Current.LocalCacheFolder.GetFileAsync(NowPlayingItem.NcPlayItem.id +
                                "." + NowPlayingItem.NcPlayItem.subext);
                        if ((await sf.GetBasicPropertiesAsync()).Size.ToString() == NowPlayingItem.NcPlayItem.size)
                            ms = MediaSource.CreateFromStorageFile(sf);
                        else
                            throw new Exception("文件大小不匹配");
                    }
                    catch (Exception)
                    {
                        //尝试从DownloadOperation下载
                        /*
                        try
                        {
                            
                            if (nocache) throw new Exception();
                            StorageFile destinationFile = await (await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("SongCache", CreationCollisionOption.OpenIfExists)).CreateFileAsync(
                                NowPlayingItem.NcPlayItem.sid +
                                "." + NowPlayingItem.NcPlayItem.subext, CreationCollisionOption.ReplaceExisting);
                            var downloadOperation =
                                downloader.CreateDownload(new Uri(NowPlayingItem.NcPlayItem.url), destinationFile);
                            downloadOperation.IsRandomAccessRequired = true;
                            var process = new Progress<DownloadOperation>((operation =>
                            {
                                if (operation.Progress.Status == BackgroundTransferStatus.Error)
                                {
                                    nocache = true;
                                    Debug.WriteLine("Download Operation Failed");
                                }
                            }));
                            _ = downloadOperation.StartAsync().AsTask(process);
                            ms = MediaSource.CreateFromDownloadOperation(downloadOperation);
                        }
                        catch
                        {
    
                        }*/
                        //本地文件的话尝试加载
                        if (NowPlayingItem.NcPlayItem.hasLocalFile)
                        {
                            ms = MediaSource.CreateFromStorageFile(NowPlayingItem.NcPlayItem.LocalStorageFile);
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(NowPlayingItem.NcPlayItem.url) ||
                                ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"] != null &&
                                ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"].ToString() != "false")
                            {
                                var (isok, json) = await Common.ncapi.RequestAsync(
                                    CloudMusicApiProviders.SongUrl,
                                    new Dictionary<string, object>
                                    {
                                        {"id", NowPlayingItem.NcPlayItem.id},
                                        {"br", Common.Setting.audioRate}
                                    });
                                if (isok && json["data"][0]["code"].ToString() == "200")
                                {
                                    ms = MediaSource.CreateFromUri(new Uri(json["data"][0]["url"].ToString()));
                                }
                                else
                                {
                                    PlayerOnMediaFailed(Player, null); //传一个播放失败\
                                    return;
                                }
                            }
                            else
                            {
                                ms = MediaSource.CreateFromUri(new Uri(NowPlayingItem.NcPlayItem.url));
                            }
                        }
                    }

                    break;
                case HyPlayItemType.Local:
                    ms = MediaSource.CreateFromStorageFile(NowPlayingItem.AudioInfo.LocalSongFile);
                    break;
                default:
                    ms = null;
                    break;
            }

            Player.Source = ms;
            MediaSystemControls.IsEnabled = true;
            Player.Play();
        }

        private static async void Player_SourceChanged(MediaPlayer sender, object args)
        {
            if (List.Count <= NowPlaying) return;
            //我们先把进度给放到最开始,免得炸
            Player.PlaybackSession.Position = TimeSpan.Zero;
            //当加载一个新的播放文件时,此时你应当加载歌词和SMTC
            //加载SMTC
            ControlsDisplayUpdater.Type = MediaPlaybackType.Music;
            ControlsDisplayUpdater.MusicProperties.Artist = NowPlayingItem.AudioInfo.Artist;
            ControlsDisplayUpdater.MusicProperties.AlbumTitle = NowPlayingItem.AudioInfo.Album;
            ControlsDisplayUpdater.MusicProperties.Title = NowPlayingItem.AudioInfo.SongName;
            //因为加载图片可能会高耗时,所以在此处加载
            Common.Invoke(() => OnPlayItemChange?.Invoke(NowPlayingItem));
            //加载歌词
            LoadLyrics(NowPlayingItem);
            ControlsDisplayUpdater.Thumbnail = NowPlayingItem.ItemType == HyPlayItemType.Local
                ? RandomAccessStreamReference.CreateFromStream(
                    await NowPlayingItem.AudioInfo.LocalSongFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999))
                : RandomAccessStreamReference.CreateFromUri(new Uri(NowPlayingItem.NcPlayItem.Album.cover + "?param=" +
                                                                    StaticSource.PICSIZE_AUDIO_PLAYER_COVER));
            ControlsDisplayUpdater.Update();
        }

        private static void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            Common.Invoke(() => OnPlayPositionChange?.Invoke(Player.PlaybackSession.Position));
            LoadLyricChange();
        }

        private static void LoadLyricChange()
        {
            if (Lyrics.Count == 0) return;
            if (lyricpos >= Lyrics.Count || lyricpos < 0) lyricpos = 0;
            var changed = false;
            if (Lyrics[lyricpos].LyricTime > Player.PlaybackSession.Position) //当感知到进度回溯时执行
            {
                lyricpos = Lyrics.FindLastIndex(t => t.LyricTime <= Player.PlaybackSession.Position) - 1;
                if (lyricpos == -2) lyricpos = -1;

                changed = true;
            }

            try
            {
                if (lyricpos == 0 && Lyrics.Count != 1) changed = true;
                while (Lyrics.Count > lyricpos + 1 &&
                       Lyrics[lyricpos + 1].LyricTime <= Player.PlaybackSession.Position) //正常的滚歌词
                {
                    lyricpos++;
                    changed = true;
                }
            }
            catch
            {
            }


            if (changed) Common.Invoke(() => OnLyricChange?.Invoke());
        }

        private static void Player_VolumeChanged(MediaPlayer sender, object args)
        {
            Common.Setting.Volume = (int)(Player.Volume * 100);
            Common.Invoke(() => OnVolumeChange?.Invoke(Player.Volume));
        }

        private static void Player_CurrentStateChanged(MediaPlayer sender, object args)
        {
            //先通知SMTC
            switch (Player.PlaybackSession.PlaybackState)
            {
                case MediaPlaybackState.Playing:
                    crashedTime = 0;
                    MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    break;
                case MediaPlaybackState.Paused:
                    MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
            }

            if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                Common.Invoke(() => OnPlay?.Invoke());
            else
                Common.Invoke(() => OnPause?.Invoke());
        }

        public static async void LoadLyrics(HyPlayItem hpi)
        {
            if (hpi.ItemType == HyPlayItemType.Netease && hpi.AudioInfo.Lyric == null)
            {
                var lrcs = await LoadNCLyric(hpi);
                hpi.AudioInfo.Lyric = lrcs.PureLyrics;
                hpi.AudioInfo.TrLyric = lrcs.TrLyrics;
            }

            //先进行歌词转换以免被搞
            Lyrics = Utils.ConvertPureLyric(hpi.AudioInfo.Lyric);
            Utils.ConvertTranslation(hpi.AudioInfo.TrLyric, Lyrics);
            if (Lyrics.Count != 0 && Lyrics[0].LyricTime != TimeSpan.Zero)
                Lyrics.Insert(0,
                    new SongLyric { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "" });
            lyricpos = 0;
            Common.Invoke(() => OnLyricLoaded?.Invoke());
        }

        public static async Task<PureLyricInfo> LoadNCLyric(HyPlayItem ncp)
        {
            try
            {
                if (ncp.ItemType != HyPlayItemType.Netease)
                    return new PureLyricInfo
                    {
                        PureLyrics = "[00:00.000] 无歌词 请欣赏",
                        TrLyrics = null
                    };

                var (isOk, json) = await Common.ncapi.RequestAsync(
                    CloudMusicApiProviders.Lyric,
                    new Dictionary<string, object> { { "id", ncp.NcPlayItem.id } });
                if (isOk)
                {
                    if (json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true")
                        return new PureLyricInfo
                        {
                            PureLyrics = "[00:00.000] 纯音乐 请欣赏",
                            TrLyrics = null
                        };
                    if (json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true")
                        return new PureLyricInfo
                        {
                            PureLyrics = "[00:00.000] 无歌词 请欣赏",
                            TrLyrics = null
                        };
                    try
                    {
                        return new PureLyricInfo
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
            catch
            {
                return new PureLyricInfo();
            }

            return new PureLyricInfo();
        }

        /********        播放文件相关        ********/

        public static async Task<HyPlayItem> AppendNCSong(NCSong ncSong)
        {
            var hpi = await LoadNCSong(ncSong);
            if (hpi != null)
                List.Add(hpi);
            return hpi;
        }

        public static async Task<HyPlayItem> LoadNCSong(NCSong ncSong)
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(
                CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object> { { "id", ncSong.sid }, { "br", Common.Setting.audioRate } });
            if (isOk)
                try
                {
                    if (json["data"][0]["code"].ToString() != "200") return null; //未获取到

                    var tag = "";
                    if (json["data"][0]["type"].ToString().ToLowerInvariant() == "flac")
                        tag = "SQ";
                    else
                        tag = json["data"][0]["br"].ToObject<int>() / 1000 + "k";

                    var ncp = new NCPlayItem
                    {
                        Type = ncSong.Type,
                        bitrate = json["data"][0]["br"].ToObject<int>(),
                        tag = tag,
                        Album = ncSong.Album,
                        Artist = ncSong.Artist,
                        subext = json["data"][0]["type"].ToString().ToLowerInvariant(),
                        id = ncSong.sid,
                        songname = ncSong.songname,
                        url = json["data"][0]["url"].ToString(),
                        LengthInMilliseconds = ncSong.LengthInMilliseconds,
                        size = json["data"][0]["size"].ToString(),
                        md5 = json["data"][0]["md5"].ToString()
                    };
                    return LoadNCPlayItem(ncp);
                }
                catch
                {
                    return null;
                }

            return null;
        }

        public static HyPlayItem AppendNCPlayItem(NCPlayItem ncp)
        {
            var hpi = LoadNCPlayItem(ncp);
            List.Add(hpi);
            return hpi;
        }

        public static HyPlayItem LoadNCPlayItem(NCPlayItem ncp)
        {
            var ai = new AudioInfo
            {
                Album = ncp.Album.name,
                ArtistArr = ncp.Artist.Select(artist => artist.name).ToArray(),
                Artist = string.Join(" / ", ncp.Artist.Select(artist => artist.name)),
                LengthInMilliseconds = ncp.LengthInMilliseconds,
                Picture = ncp.Album.cover,
                SongName = ncp.songname,
                tag = ncp.tag
            };

            var hpi = new HyPlayItem
            {
                AudioInfo = ai,
                ItemType = ncp.Type,
                Name = ncp.songname,
                NcPlayItem = ncp,
                Path = ncp.url
            };
            Common.GLOBAL["PERSONALFM"] = "false";
            return hpi;
        }

        public static async Task<bool> AppendFile(StorageFile sf, bool nocheck163 = false)
        {
            The163KeyStruct mi;
            if (nocheck163 ||
                !The163KeyHelper.TryGetMusicInfo(File.Create(new UwpStorageFileAbstraction(sf)).Tag, out mi))
            {
                //TagLib.File afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
                var mdp = await sf.Properties.GetMusicPropertiesAsync();
                string[] contributingArtistsKey = { "System.Music.Artist" };
                var contributingArtistsProperty =
                    await mdp.RetrievePropertiesAsync(contributingArtistsKey);
                var contributingArtists = contributingArtistsProperty["System.Music.Artist"] as string[];
                if (contributingArtists is null) contributingArtists = new[] { "未知歌手" };

                var ai = new AudioInfo
                {
                    tag = "本地",
                    Album = string.IsNullOrEmpty(mdp.Album) ? "未知专辑" : mdp.Album,
                    ArtistArr = contributingArtists,
                    Artist = string.IsNullOrEmpty(string.Join('/', contributingArtists))
                        ? "未知歌手"
                        : string.Join('/', contributingArtists),
                    LengthInMilliseconds = mdp.Duration.TotalMilliseconds,
                    SongName = string.IsNullOrEmpty(mdp.Title) ? sf.DisplayName : mdp.Title,
                    LocalSongFile = sf
                };

                //记载歌词
                try
                {
                    var lrcfile =
                        await (await sf.GetParentAsync()).GetFileAsync(Path.ChangeExtension(sf.Name, "lrc"));
                    ai.Lyric = await FileIO.ReadTextAsync(lrcfile);
                }
                catch (Exception)
                {
                }

                var hyPlayItem = new HyPlayItem
                {
                    AudioInfo = ai,
                    ItemType = HyPlayItemType.Local,
                    Name = ai.SongName,
                    Path = sf.Path
                };

                List.Add(hyPlayItem);
                return true;
            }

            if (string.IsNullOrEmpty(mi.musicName)) return await AppendFile(sf, true);

            var hpi = new NCPlayItem
            {
                Album = new NCAlbum
                {
                    name = mi.album,
                    id = mi.albumId.ToString(),
                    cover = mi.albumPic
                },
                bitrate = mi.bitrate,
                hasLocalFile = true,
                LocalStorageFile = sf,
                Type = HyPlayItemType.Netease,
                LengthInMilliseconds = mi.duration,
                id = mi.musicId.ToString(),
                Artist = null,
                md5 = null,
                size = sf.GetBasicPropertiesAsync().GetAwaiter().GetResult().Size.ToString(),
                songname = mi.musicName,
                tag = "本地"
            };
            hpi.Artist = mi.artist.Select(t => new NCArtist { name = t[0].ToString(), id = t[1].ToString() })
                .ToList();
            AppendNCPlayItem(hpi);
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
            var Lyrics = new List<SongLyric>();
            if (string.IsNullOrEmpty(LyricAllText)) return new List<SongLyric> { SongLyric.NoLyric };

            var LyricsArr = LyricAllText.Replace("\r\n", "\n").Replace("\r", "\n").Split("\n");
            var offset = TimeSpan.Zero;
            foreach (var sL in LyricsArr)
            {
                var LyricTextLine = sL.Trim();
                if (LyricTextLine.IndexOf('[') == -1 || LyricTextLine.IndexOf(']') == -1) continue; //此行不为Lrc

                var prefix = LyricTextLine.Substring(1, LyricTextLine.IndexOf(']') - 1);
                if (prefix.StartsWith("al") || prefix.StartsWith("ar") || prefix.StartsWith("au") ||
                    prefix.StartsWith("by") || prefix.StartsWith("re") || prefix.StartsWith("ti") ||
                    prefix.StartsWith("ve"))
                    //这种废标签不想解析
                    continue;

                if (prefix.StartsWith("offset"))
                {
                    if (!int.TryParse(prefix.Substring(6), out var offsetint)) continue;

                    offset = new TimeSpan(0, 0, 0, 0, offsetint);
                }

                if (!TimeSpan.TryParse("00:" + prefix, out var time)) continue;

                var lrctxt = LyricTextLine.Substring(LyricTextLine.IndexOf(']') + 1);
                while (lrctxt.Trim().StartsWith('['))
                {
                    //一句双时间
                    Lyrics = Lyrics.Union(ConvertPureLyric(lrctxt)).ToList();
                    lrctxt = lrctxt.Substring(LyricTextLine.IndexOf(']') + 1);
                }

                string translation = null;
                /*
                //NLyric 的双语歌词 - 夹带私货

                if (LyricTextLine.IndexOf('「') != -1 && LyricTextLine.IndexOf('」') != -1)
                {
                    translation = LyricTextLine.Substring(LyricTextLine.IndexOf('「') + 1,
                        LyricTextLine.IndexOf('」') - LyricTextLine.IndexOf('「') - 1);
                    lrctxt = lrctxt.Substring(0, lrctxt.IndexOf('「'));
                }
                */
                var HaveTranslation = !string.IsNullOrEmpty(translation);
                Lyrics.Add(new SongLyric
                {
                    LyricTime = time + offset,
                    PureLyric = lrctxt,
                    Translation = translation,
                    HaveTranslation = HaveTranslation
                });
            }

            return Lyrics.OrderBy(lyric => lyric.LyricTime.TotalMilliseconds).ToList();
        }

        public static void ConvertTranslation(string LyricAllText, List<SongLyric> Lyrics)
        {
            if (string.IsNullOrEmpty(LyricAllText)) return;

            var LyricsArr = LyricAllText.Replace("\r\n", "\n").Replace("\r", "\n").Split("\n");
            var offset = TimeSpan.Zero;
            foreach (var sL in LyricsArr)
            {
                var LyricTextLine = sL.Trim();
                if (LyricTextLine.IndexOf('[') == -1 || LyricTextLine.IndexOf(']') == -1) continue; //此行不为Lrc

                var prefix = LyricTextLine.Substring(1, LyricTextLine.IndexOf(']') - 1);
                if (prefix.StartsWith("al") || prefix.StartsWith("ar") || prefix.StartsWith("au") ||
                    prefix.StartsWith("by") || prefix.StartsWith("re") || prefix.StartsWith("ti") ||
                    prefix.StartsWith("ve"))
                    //这种废标签不想解析
                    continue;

                if (prefix.StartsWith("offset"))
                {
                    if (!int.TryParse(prefix.Substring(6), out var offsetint)) continue;

                    offset = new TimeSpan(0, 0, 0, 0, offsetint);
                }

                if (!TimeSpan.TryParse("00:" + prefix, out var time)) continue;

                var lrctxt = LyricTextLine.Substring(LyricTextLine.IndexOf(']') + 1);

                while (lrctxt.Trim().StartsWith('['))
                {
                    //一句双时间
                    ConvertTranslation(lrctxt, Lyrics);
                    lrctxt = lrctxt.Substring(LyricTextLine.IndexOf(']') + 1);
                }

                for (var i = 0; i < Lyrics.Count; i++)
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
}