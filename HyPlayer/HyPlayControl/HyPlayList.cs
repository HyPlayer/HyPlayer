using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Windows.Foundation;
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
using Windows.System.Profile;

namespace HyPlayer.HyPlayControl
{
    public static class HyPlayList
    {
        private static int GCCountDown = 5;

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
        public static PlayMode NowPlayType
        {
            set { Common.Setting.songRollType = ((int)value); }

            get { return (PlayMode)Common.Setting.songRollType; }
        }

        public static int NowPlaying;
        public static Timer SecTimer = new Timer(1000); // 公用秒表
        public static readonly List<HyPlayItem> List = new List<HyPlayItem>();
        public static List<SongLyric> Lyrics = new List<SongLyric>();
        public static TimeSpan LyricOffset = TimeSpan.Zero;

        /********        API        ********/
        public static MediaPlayer Player;
        public static SystemMediaTransportControls MediaSystemControls;
        public static SystemMediaTransportControlsDisplayUpdater ControlsDisplayUpdater;
        public static BackgroundDownloader downloader = new BackgroundDownloader();

        public static int lyricpos;
        public static string crashedTime;
        public static bool isPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        private static string _lastStorageUrl;
        private static StorageFile _lastStorageFile;

        public static StorageFile NowPlayingStorageFile => _lastStorageFile;


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
            Player.CommandManager.IsEnabled = Common.Setting.ancientSMTC;
            MediaSystemControls.IsPlayEnabled = true;
            MediaSystemControls.IsPauseEnabled = true;
            MediaSystemControls.IsNextEnabled = true;
            MediaSystemControls.IsPreviousEnabled = true;
            MediaSystemControls.IsEnabled = true;
            MediaSystemControls.ButtonPressed += SystemControls_ButtonPressed;
            MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
            //MediaSystemControls.PlaybackPositionChangeRequested += MediaSystemControls_PlaybackPositionChangeRequested;
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
                Common.Invoke(() =>
                {
                    if (--GCCountDown < 0)
                    {
                        GCCountDown = 5;
                        GC.Collect();
                    }
                });
            };
            HistoryManagement.InitializeHistoryTrack();
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
            Common.ShowTeachingTip("播放失败 正在重试", args.ErrorMessage);
            if (crashedTime == NowPlayingItem.PlayItem.url)
            {
                SongMoveNext();
                crashedTime = "jump";
            }
            else
            {
                crashedTime = NowPlayingItem.PlayItem.url;
                if ((NowPlayingItem.ItemType == HyPlayItemType.Netease && !NowPlayingItem.PlayItem.isLocalFile) ||
                    NowPlayingItem.ItemType == HyPlayItemType.Radio)
                    //TODO FM和普通歌曲一起
                    Common.Invoke(async () =>
                    {
                        List[NowPlaying] = LoadNCSong(new NCSong
                        {
                            Type = NowPlayingItem.ItemType,
                            Album = NowPlayingItem.PlayItem.Album,
                            Artist = NowPlayingItem.PlayItem.Artist,
                            LengthInMilliseconds = NowPlayingItem.PlayItem.LengthInMilliseconds,
                            sid = NowPlayingItem.PlayItem.id,
                            songname = NowPlayingItem.PlayItem.Name
                        });
                        LoadPlayerSong();
                        Player.Play();
                    });
                else
                    //本地歌曲炸了的话就Move下一首吧
                    SongMoveNext();
            }

            //Player.PlaybackSession.Position = temppos;
        }

        private static async Task<StorageFile> LoadLocalFile()
        {
            // 此处可以改进
            if (_lastStorageUrl != NowPlayingItem.PlayItem.url)
            {
                if (NowPlayingItem.PlayItem.DontSetLocalStorageFile != null)
                {
                    if (NowPlayingItem.PlayItem.DontSetLocalStorageFile.FileType != ".ncm")
                    {
                        _lastStorageFile = NowPlayingItem.PlayItem.DontSetLocalStorageFile;
                    }
                    else
                    {
                        // 脑残Music解析
                        Stream stream = (await NowPlayingItem.PlayItem.DontSetLocalStorageFile.OpenReadAsync()).AsStreamForRead();
                        if (NCMFile.IsCorrectNCMFile(stream))
                        {
                            var Info = NCMFile.GetNCMMusicInfo(stream);
                            var CoverStream = NCMFile.GetCoverStream(stream);
                            var encStream = NCMFile.GetEncryptedStream(stream);
                            encStream.Seek(0, SeekOrigin.Begin);
                            _lastStorageFile = await StorageFile.CreateStreamedFileAsync(
                                    Path.ChangeExtension(NowPlayingItem.PlayItem.DontSetLocalStorageFile.Name,
                                        Info.format), (t) => { encStream.CopyTo(t.AsStreamForWrite()); },
                                    RandomAccessStreamReference.CreateFromStream(
                                        CoverStream.AsRandomAccessStream()));
                        }
                    }
                }
                else
                {
                    _lastStorageUrl = NowPlayingItem.PlayItem.url;
                    _lastStorageFile = await StorageFile.GetFileFromPathAsync(NowPlayingItem.PlayItem.url);
                }
            }
            Player_SourceChanged(null, null);
            return _lastStorageFile;
        }

        /********        方法         ********/
        public static void SongAppendDone()
        {
            Common.GLOBAL["PERSONALFM"] = "false";
            Common.Invoke(() => OnPlayListAddDone?.Invoke());
        }

        public static void SongMoveNext()
        {
            Common.Invoke(() => OnSongMoveNext?.Invoke());
            if (List.Count == 0) return;
            MoveSongPointer(true);
            LoadPlayerSong();
            Player.Play();
        }

        public static void SongMovePrevious()
        {
            if (List.Count == 0) return;
            if (NowPlaying - 1 < 0)
                NowPlaying = List.Count - 1;
            else
                NowPlaying--;

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
            if (List.Count - 1 == 0)
            {
                RemoveAllSong();
                return;
            }

            if (index == NowPlaying)
            {
                List.RemoveAt(index);
                LoadPlayerSong();
                Player.Play();
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

        public static async void LoadPlayerSong()
        {
            if (NowPlayingItem.PlayItem?.Name == null)
            {
                MoveSongPointer();
                return;
            }

            Player_SourceChanged(null, null);
            MediaSource ms;
            switch (NowPlayingItem.ItemType)
            {
                case HyPlayItemType.Netease:
                case HyPlayItemType.Radio: //FM伪加载为普通歌曲
                case HyPlayItemType.Pan: //云盘接口也是这个

                    //先看看是不是本地文件
                    //本地文件的话尝试加载
                    //cnm的NCM,我试试其他方式
                    if (NowPlayingItem.PlayItem.isLocalFile)
                    {
                        await LoadLocalFile();
                        ms = MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
                    }
                    else
                    {
                        string playUrl = NowPlayingItem.PlayItem.url;
                        // 对了,先看看是否要刷新播放链接
                        if (string.IsNullOrEmpty(NowPlayingItem.PlayItem.url) ||
                            Common.Setting.songUrlLazyGet)
                        {
                            try
                            {
                                var json = await Common.ncapi.RequestAsync(
                                    CloudMusicApiProviders.SongUrl,
                                    new Dictionary<string, object>
                                    {
                                        { "id", NowPlayingItem.PlayItem.id },
                                        { "br", Common.Setting.audioRate }
                                    });
                                if (json["data"][0]["code"].ToString() == "200")
                                {
                                    playUrl = json["data"][0]["url"].ToString();
                                }
                                else
                                {
                                    PlayerOnMediaFailed(Player, null); //传一个播放失败\
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                PlayerOnMediaFailed(Player, null); //传一个播放失败\
                                return;
                            }
                        }

                        if (Common.Setting.enableCache)
                        {
                            //再检测是否已经缓存且大小正常
                            try
                            {
                                // 加载本地缓存文件
                                var sf =
                                    await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir))
                                        .GetFileAsync(NowPlayingItem.PlayItem.id +
                                                      "." + NowPlayingItem.PlayItem.subext);
                                if ((await sf.GetBasicPropertiesAsync()).Size.ToString() ==
                                    NowPlayingItem.PlayItem.size)
                                    ms = MediaSource.CreateFromStorageFile(sf);
                                else
                                    throw new Exception("File Size Not Match");
                            }
                            catch
                            {
                                try
                                {
                                    //尝试从DownloadOperation下载
                                    StorageFile destinationFile =
                                        await (await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
                                            "songCache",
                                            CreationCollisionOption.OpenIfExists)).CreateFileAsync(
                                            NowPlayingItem.PlayItem.id +
                                            "." + NowPlayingItem.PlayItem.subext,
                                            CreationCollisionOption.ReplaceExisting);
                                    var downloadOperation =
                                        downloader.CreateDownload(new Uri(playUrl), destinationFile);
                                    downloadOperation.IsRandomAccessRequired = true;
                                    ms = MediaSource.CreateFromDownloadOperation(downloadOperation);
                                }
                                catch
                                {
                                    ms = MediaSource.CreateFromUri(new Uri(playUrl));
                                }
                            }
                        }
                        else
                        {
                            ms = MediaSource.CreateFromUri(new Uri(playUrl));
                        }
                    }

                    break;
                case HyPlayItemType.Local:
                    try
                    {
                        await LoadLocalFile();
                        ms = MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
                    }
                    catch
                    {
                        ms = MediaSource.CreateFromUri(new Uri(NowPlayingItem.PlayItem.url));
                    }

                    break;
                default:
                    ms = null;
                    break;
            }

            MediaSystemControls.IsEnabled = true;
            Player.Source = ms;
            //Player.Play();
        }

        public static async void Player_SourceChanged(MediaPlayer sender, object args)
        {
            if (List.Count <= NowPlaying) return;
            //我们先把进度给放到最开始,免得炸
            Player.Pause();
            Player.PlaybackSession.Position = TimeSpan.Zero;
            //当加载一个新的播放文件时,此时你应当加载歌词和SMTC
            //加载SMTC
            ControlsDisplayUpdater.Type = MediaPlaybackType.Music;
            ControlsDisplayUpdater.MusicProperties.Artist = NowPlayingItem.PlayItem.ArtistString;
            ControlsDisplayUpdater.MusicProperties.AlbumTitle = NowPlayingItem.PlayItem.AlbumString;
            ControlsDisplayUpdater.MusicProperties.Title = NowPlayingItem.PlayItem.Name;
            //记录下当前播放位置
            ApplicationData.Current.LocalSettings.Values["nowSongPointer"] = NowPlaying.ToString();
            //因为加载图片可能会高耗时,所以在此处加载
            Common.Invoke(() => OnPlayItemChange?.Invoke(NowPlayingItem));
            //加载歌词
            LoadLyrics(NowPlayingItem);
            ControlsDisplayUpdater.Thumbnail = NowPlayingItem.ItemType == HyPlayItemType.Local
                ? RandomAccessStreamReference.CreateFromStream(
                    await NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999))
                : RandomAccessStreamReference.CreateFromUri(new Uri(NowPlayingItem.PlayItem.Album.cover + "?param=" +
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
            var realpos = Player.PlaybackSession.Position - LyricOffset;
            if (Lyrics[lyricpos].LyricTime > realpos) //当感知到进度回溯时执行
            {
                lyricpos = Lyrics.FindLastIndex(t => t.LyricTime <= realpos) - 1;
                if (lyricpos == -2) lyricpos = -1;

                changed = true;
            }

            try
            {
                if (lyricpos == 0 && Lyrics.Count != 1) changed = false;
                while (Lyrics.Count > lyricpos + 1 &&
                       Lyrics[lyricpos + 1].LyricTime <= realpos) //正常的滚歌词
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
            if (!Common.BarPlayBar.FadeSettedVolume)
                Common.Setting.Volume = (int)(Player.Volume * 100);

            Common.Invoke(() => OnVolumeChange?.Invoke(Player.Volume));
        }

        private static void Player_CurrentStateChanged(MediaPlayer sender, object args)
        {
            //先通知SMTC
            switch (Player.PlaybackSession.PlaybackState)
            {
                case MediaPlaybackState.Playing:
                    crashedTime = "playing";
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
            PureLyricInfo lrcs = new PureLyricInfo();
            if (hpi.ItemType == HyPlayItemType.Netease || hpi.ItemType == HyPlayItemType.Pan)
            {
                lrcs = await LoadNCLyric(hpi);
            }
            else if (hpi.ItemType == HyPlayItemType.Local)
            {
                try
                {
                    lrcs = new PureLyricInfo()
                    {
                        PureLyrics = await FileIO.ReadTextAsync(
                            await StorageFile.GetFileFromPathAsync(Path.ChangeExtension(NowPlayingItem.PlayItem.url,
                                "lrc")))
                    };
                }
                catch
                {
                    lrcs = new PureLyricInfo();
                }
            }

            //先进行歌词转换以免被搞
            Lyrics = Utils.ConvertPureLyric(lrcs.PureLyrics);
            Utils.ConvertTranslation(lrcs.TrLyrics, Lyrics);
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
                if ((ncp.ItemType != HyPlayItemType.Netease && ncp.ItemType != HyPlayItemType.Pan) || ncp.PlayItem == null)
                    return new PureLyricInfo
                    {
                        PureLyrics = "[00:00.000] 无歌词 请欣赏",
                        TrLyrics = null
                    };
                try
                {
                    var json = await Common.ncapi.RequestAsync(
                        CloudMusicApiProviders.Lyric,
                        new Dictionary<string, object> { { "id", ncp.PlayItem.id } });

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
                catch (Exception ex)
                {
                    Common.ShowTeachingTip("发生错误", ex.Message);
                }
            }
            catch
            {
                return new PureLyricInfo();
            }

            return new PureLyricInfo();
        }

        /********        播放文件相关        ********/

        public static async Task<HyPlayItem> AppendNCSong(NCSong ncSong, int position = -1)
        {
            var hpi = LoadNCSong(ncSong);
            if (position < 0)
                position = List.Count;
            if (hpi != null)
                List.Insert(position, hpi);
            return hpi;
        }

        public static HyPlayItem LoadNCSong(NCSong ncSong)
        {
            try
            {
                var ncp = new PlayItem
                {
                    Type = ncSong.Type,
                    //bitrate = json["data"][0]["br"].ToObject<int>(),
                    tag = "在线",
                    Album = ncSong.Album,
                    Artist = ncSong.Artist,
                    //subext = json["data"][0]["type"].ToString().ToLowerInvariant(),
                    id = ncSong.sid,
                    Name = ncSong.songname,
                    //url = json["data"][0]["url"].ToString(),
                    LengthInMilliseconds = ncSong.LengthInMilliseconds,
                    //size = json["data"][0]["size"].ToString(),
                    //md5 = json["data"][0]["md5"].ToString()
                };
                return LoadNCPlayItem(ncp);
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("发生错误", ex.Message);
            }

            return null;
        }

        public static HyPlayItem AppendNCPlayItem(PlayItem ncp)
        {
            var hpi = LoadNCPlayItem(ncp);
            List.Add(hpi);
            return hpi;
        }

        public static HyPlayItem LoadNCPlayItem(PlayItem ncp)
        {
            var hpi = new HyPlayItem
            {
                ItemType = ncp.Type,
                PlayItem = ncp,
            };
            return hpi;
        }

        public static async Task<bool> AppendNCSongs(IList<NCSong> NCSongs, HyPlayItemType itemType = HyPlayItemType.Netease,
             bool needRemoveList = true)
        {
            if (NCSongs == null)
                return false;
            if (needRemoveList)
                HyPlayList.RemoveAllSong();
            try
            {

                for (var i = 0; i < NCSongs.Count; i++)
                {
                    var ncSong = NCSongs[i];
                    var ncp = new PlayItem
                    {
                        Type = itemType,
                        tag = "在线",
                        Album = ncSong.Album,
                        Artist = ncSong.Artist,
                        //subext = token["type"].ToString(),
                        id = ncSong.sid,
                        Name = ncSong.songname,
                        //url = token["url"].ToString(),
                        LengthInMilliseconds = ncSong.LengthInMilliseconds,
                        //size = token["size"].ToString(),
                        //md5 = token["md5"].ToString()
                    };
                    var item = AppendNCPlayItem(ncp);
                }

                return true;
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("发生错误", ex.Message);
            }

            return false;
        }

        public static async Task<bool> AppendNCSource(string sourceId)
        {
            /*  歌单: pl+歌单ID (e.g. pl123456)
             *  单曲: ns+歌曲ID (e.g. ns1515584)
             *  专辑: al + 专辑ID(e.g.al552255)
             *  歌手热门: sh + 歌手ID(e.g sh25151)
             *  歌手全部: sa + 歌手ID e.g.sa245144
             *  电台: rd + 电台ID  e.g.rd5274522
             *  最近播放: rc + 随机数字
             */
            string prefix = sourceId.Substring(0, 2);
            switch (prefix)
            {
                case "pl":
                    return await AppendPlayList(sourceId.Substring(2, sourceId.Length - 2));
                case "ns":
                    _ = AppendNCSong(NCSong.CreateFromJson((await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail, new Dictionary<string, object>() { { "id", sourceId.Substring(2, sourceId.Length - 2) } }))["songs"][0]));
                    return true;
                case "al":
                    return await AppendAlbum(sourceId.Substring(2, sourceId.Length - 2));
                case "sh":
                case "sa":
                case "rd":
                case "rc":
                default:
                    return false;
            }
        }

        public static async Task<bool> AppendAlbum(string alid)
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Album,
                    new Dictionary<string, object> { { "id", alid } });

                List<NCSong> list = new List<NCSong>();
                foreach (var song in json["songs"].ToArray())
                {
                    var ncSong = NCSong.CreateFromJson(song);
                    list.Add(ncSong);
                }
                list.RemoveAll(t => t == null);
                await HyPlayList.AppendNCSongs(list, HyPlayItemType.Netease, false);
                

                return true;
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("发生错误", ex.Message);
            }

            return false;
        }
        public static async Task<bool> AppendPlayList(string plid)
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                    new Dictionary<string, object> { { "id", plid } });

                int nowidx = 0;
                var trackIds = json["playlist"]["trackIds"].Select(t => (int)t["id"]).ToList();
                while (nowidx * 500 < trackIds.Count)
                {
                    var nowIds = trackIds.GetRange(nowidx * 500, Math.Min(500, trackIds.Count - nowidx * 500));
                    try
                    {
                        json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                            new Dictionary<string, object> { ["ids"] = string.Join(",", nowIds) });
                        nowidx++;
                        var i = 0;
                        var ncSongs = json["songs"].Select(t =>
                        {
                            if (json["privileges"].ToList()[i++]["st"].ToString() == "0")
                            {
                                return NCSong.CreateFromJson(t);
                            }

                            return null;
                        }).ToList();
                        ncSongs.RemoveAll(t => t == null);
                        await HyPlayList.AppendNCSongs(ncSongs, HyPlayItemType.Netease, false);
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("发生错误", ex.Message);
            }

            return false;
        }

        public static async Task<bool> AppendStorageFile(StorageFile sf, bool nocheck163 = false)
        {
            List.Add(await LoadStorageFile(sf));
            return true;
        }

        public static async Task<HyPlayItem> LoadStorageFile(StorageFile sf, bool nocheck163 = false)
        {
            The163KeyClass mi;
            var mdp = await sf.Properties.GetMusicPropertiesAsync();

            if (nocheck163 ||
                !The163KeyHelper.TryGetMusicInfo(File.Create(new UwpStorageFileAbstraction(sf)).Tag, out mi))
            {
                //TagLib.File afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
                string[] contributingArtistsKey = { "System.Music.Artist" };
                var contributingArtistsProperty =
                    await mdp.RetrievePropertiesAsync(contributingArtistsKey);
                var contributingArtists = contributingArtistsProperty["System.Music.Artist"] as string[];
                if (contributingArtists is null) contributingArtists = new[] { "未知歌手" };


                var hyPlayItem = new HyPlayItem
                {
                    PlayItem = new PlayItem
                    {
                        isLocalFile = true,
                        bitrate = (int)mdp.Bitrate,
                        tag = sf.Provider.DisplayName,
                        id = null,
                        Name = mdp.Title ?? sf.Name,
                        Type = HyPlayItemType.Local,
                        Artist = contributingArtists.Select(t => new NCArtist()
                        {
                            name = t
                        }).ToList(),
                        Album = new NCAlbum()
                        {
                            name = mdp.Album
                        },
                        url = sf.Path,
                        subext = sf.FileType,
                        size = "0",
                        LengthInMilliseconds = mdp.Duration.TotalMilliseconds
                    },
                    ItemType = HyPlayItemType.Local
                };
                if (sf.Provider.Id == "network")
                    hyPlayItem.PlayItem.DontSetLocalStorageFile = sf;
                return hyPlayItem;
            }

            if (string.IsNullOrEmpty(mi.musicName)) return await LoadStorageFile(sf, true);

            var hpi = new PlayItem
            {
                Album = new NCAlbum
                {
                    name = mi.album,
                    id = mi.albumId.ToString(),
                    cover = mi.albumPic
                },
                url = sf.Path,
                subext = sf.FileType,
                bitrate = mi.bitrate,
                isLocalFile = true,
                Type = HyPlayItemType.Netease,
                LengthInMilliseconds = mdp.Duration.TotalMilliseconds,
                id = mi.musicId.ToString(),
                Artist = null,
                size = sf.GetBasicPropertiesAsync()
                    .GetAwaiter()
                    .GetResult()
                    .Size.ToString(),
                Name = mi.musicName,
                tag = sf.Provider.DisplayName
            };
            hpi.Artist = mi.artist.Select(t => new NCArtist { name = t[0].ToString(), id = t[1].ToString() })
                .ToList();
            if (sf.Provider.Id == "network")
                hpi.DontSetLocalStorageFile = sf;
            return new HyPlayItem()
            {
                ItemType = HyPlayItemType.Local,
                PlayItem = hpi
            };
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